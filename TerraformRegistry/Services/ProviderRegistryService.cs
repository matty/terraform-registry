using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Services;

public sealed class ProviderRegistryService : IProviderRegistryService
{
    private static readonly HashSet<int> SupportedProtocolMajors = [5, 6];
    private readonly ILogger<ProviderRegistryService> _logger;
    private readonly IProviderRepository _repository;
    private readonly IProviderArtifactStorage _storage;
    private readonly IProviderPackageValidator _validator;
    private readonly ProviderUploadOptions _uploadOptions;

    public ProviderRegistryService(
        IProviderRepository repository,
        IProviderArtifactStorage storage,
        IProviderPackageValidator validator,
        ILogger<ProviderRegistryService> logger,
        ProviderUploadOptions? uploadOptions = null)
    {
        _repository = repository;
        _storage = storage;
        _validator = validator;
        _logger = logger;
        _uploadOptions = uploadOptions ?? new ProviderUploadOptions();
        _uploadOptions.Validate();
    }

    public async Task<ProviderVersionsResponse?> GetVersionsAsync(string providerNamespace, string type)
    {
        ValidateCoordinate(providerNamespace, type);

        var versions = await _repository.GetProviderVersionsAsync(providerNamespace, type);
        if (versions.Count == 0)
        {
            return null;
        }

        return new ProviderVersionsResponse { Versions = versions.ToList() };
    }

    public async Task<ProviderManagementVersionsResponse?> GetManagementVersionsAsync(string providerNamespace, string type)
    {
        ValidateCoordinate(providerNamespace, type);

        var provider = await _repository.GetProviderAsync(providerNamespace, type);
        if (provider == null)
        {
            return null;
        }

        var versions = await _repository.GetProviderManagementVersionsAsync(providerNamespace, type);
        return new ProviderManagementVersionsResponse { Versions = versions.ToList() };
    }

    public async Task<ProviderManagementPlatformsResponse?> GetManagementPlatformsAsync(string providerNamespace, string type, string version)
    {
        ValidateCoordinate(providerNamespace, type);
        ValidateVersion(version);

        var providerVersion = await _repository.GetProviderVersionAsync(providerNamespace, type, version);
        if (providerVersion == null)
        {
            return null;
        }

        var platforms = await _repository.GetProviderManagementPlatformsAsync(providerNamespace, type, version);
        return new ProviderManagementPlatformsResponse { Platforms = platforms.ToList() };
    }

    public async Task<ProviderPackageResponse?> GetPackageAsync(string providerNamespace, string type, string version, string os,
        string arch, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        ValidateCoordinate(providerNamespace, type);

        var provider = await _repository.GetProviderAsync(providerNamespace, type);
        if (provider == null)
        {
            return null;
        }

        var providerVersion = await _repository.GetProviderVersionAsync(providerNamespace, type, version);
        if (providerVersion == null ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsStoragePath) ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsSignatureStoragePath))
        {
            return null;
        }

        var platform = await _repository.GetProviderPlatformAsync(providerNamespace, type, version, os, arch);
        if (platform == null || string.IsNullOrWhiteSpace(platform.PackageStoragePath))
        {
            return null;
        }

        var gpgKey = await _repository.GetGpgKeyAsync(providerNamespace, providerVersion.KeyId);
        if (gpgKey == null)
        {
            return null;
        }

        try
        {
            var packageUrl = await _storage.CreateDownloadUrlAsync(platform.PackageStoragePath, cancellationToken);
            var shasumsUrl = await _storage.CreateDownloadUrlAsync(providerVersion.ShasumsStoragePath, cancellationToken);
            var signatureUrl = await _storage.CreateDownloadUrlAsync(providerVersion.ShasumsSignatureStoragePath, cancellationToken);

            await _repository.RecordProviderDownloadAsync(provider.Id, providerNamespace, type, version, os, arch, clientIp, userAgent);

            return new ProviderPackageResponse
            {
                Protocols = providerVersion.Protocols,
                Os = platform.Os,
                Arch = platform.Arch,
                Filename = platform.Filename,
                DownloadUrl = packageUrl,
                ShasumsUrl = shasumsUrl,
                ShasumsSignatureUrl = signatureUrl,
                Shasum = platform.Shasum,
                SigningKeys = new ProviderSigningKeys
                {
                    GpgPublicKeys =
                    [
                        new ProviderGpgPublicKey
                        {
                            KeyId = gpgKey.KeyId,
                            AsciiArmor = gpgKey.AsciiArmor,
                            TrustSignature = gpgKey.TrustSignature,
                            Source = gpgKey.Source,
                            SourceUrl = gpgKey.SourceUrl
                        }
                    ]
                }
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            RegistryLog.Warning(_logger, ex, "Provider package artifact metadata is incomplete for {Namespace}/{Type}/{Version}/{Os}/{Arch}",
                providerNamespace, type, version, os, arch);
            return null;
        }
    }

    public Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit)
    {
        return _repository.ListProvidersAsync(q, Math.Max(0, offset), Math.Clamp(limit, 1, 100));
    }

    public Task<int> CountProvidersAsync(string? q)
    {
        return _repository.CountProvidersAsync(q);
    }

    public Task<TerraformProvider?> GetProviderAsync(string providerNamespace, string type)
    {
        ValidateCoordinate(providerNamespace, type);
        return _repository.GetProviderAsync(providerNamespace, type);
    }

    public Task<TerraformProvider> CreateProviderAsync(CreateProviderRequest request, string? actorUserId)
    {
        ValidateCoordinate(request.Namespace, request.Type);

        var now = DateTime.UtcNow;
        return _repository.CreateProviderAsync(new TerraformProvider
        {
            Namespace = request.Namespace,
            Type = request.Type,
            DisplayName = request.DisplayName,
            Description = request.Description,
            SourceRepositoryUrl = request.SourceRepositoryUrl,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public Task<bool> UpdateProviderAsync(string providerNamespace, string type, UpdateProviderRequest request)
    {
        ValidateCoordinate(providerNamespace, type);
        return _repository.UpdateProviderAsync(providerNamespace, type, request.DisplayName, request.Description, request.SourceRepositoryUrl);
    }

    public Task<bool> DeleteProviderAsync(string providerNamespace, string type)
    {
        ValidateCoordinate(providerNamespace, type);
        return DeleteProviderAndArtifactsAsync(providerNamespace, type);
    }

    public Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string providerNamespace)
    {
        ValidateProviderSegment(providerNamespace, "provider namespace");
        return _repository.ListGpgKeysAsync(providerNamespace);
    }

    public Task<ProviderGpgKey> AddGpgKeyAsync(string providerNamespace, CreateProviderGpgKeyRequest request)
    {
        ValidateProviderSegment(providerNamespace, "provider namespace");
        if (string.IsNullOrWhiteSpace(request.KeyId))
            throw new ArgumentException("Provider GPG key ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AsciiArmor))
            throw new ArgumentException("Provider GPG public key is required.", nameof(request));

        return _repository.AddGpgKeyAsync(new ProviderGpgKey
        {
            Namespace = providerNamespace,
            KeyId = request.KeyId,
            AsciiArmor = request.AsciiArmor,
            TrustSignature = request.TrustSignature,
            Source = request.Source,
            SourceUrl = request.SourceUrl,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> RevokeGpgKeyAsync(string providerNamespace, string keyId)
    {
        ValidateProviderSegment(providerNamespace, "provider namespace");
        var key = await _repository.GetGpgKeyAsync(providerNamespace, keyId);
        if (key == null)
        {
            return false;
        }

        if (await _repository.ProviderGpgKeyIsReferencedByActiveVersionsAsync(providerNamespace, keyId))
        {
            throw new InvalidOperationException(
                $"Provider GPG key {keyId} is used by active provider versions and cannot be revoked until those versions are deleted.");
        }

        return await _repository.RevokeGpgKeyAsync(providerNamespace, keyId);
    }

    public async Task<ProviderVersion> CreateVersionAsync(string providerNamespace, string type, CreateProviderVersionRequest request)
    {
        ValidateCoordinate(providerNamespace, type);
        ValidateVersion(request.Version);
        ValidateProtocols(request.Protocols);

        var provider = await _repository.GetProviderAsync(providerNamespace, type) ??
                       throw new InvalidOperationException($"Provider {providerNamespace}/{type} was not found.");
        _ = await _repository.GetGpgKeyAsync(providerNamespace, request.KeyId) ??
            throw new InvalidOperationException($"Provider GPG key {request.KeyId} was not found.");

        return await _repository.CreateProviderVersionAsync(provider.Id, request.Version, request.Protocols, request.KeyId);
    }

    public async Task<bool> UploadShasumsAsync(string providerNamespace, string type, string version, Stream content,
        CancellationToken cancellationToken)
    {
        var providerVersion = await RequireProviderVersionAsync(providerNamespace, type, version);
        var saveResult = await _storage.SaveAsync(ShasumsPath(providerNamespace, type, version), content, cancellationToken);
        return await _repository.SetVersionShasumsPathAsync(providerVersion.Id, saveResult.StoragePath);
    }

    public async Task<bool> UploadShasumsSignatureAsync(string providerNamespace, string type, string version, Stream content,
        CancellationToken cancellationToken)
    {
        var providerVersion = await RequireProviderVersionAsync(providerNamespace, type, version);
        var saveResult = await _storage.SaveAsync(SignaturePath(providerNamespace, type, version), content, cancellationToken);
        return await _repository.SetVersionShasumsSignaturePathAsync(providerVersion.Id, saveResult.StoragePath);
    }

    public Task<bool> DeleteVersionAsync(string providerNamespace, string type, string version)
    {
        ValidateCoordinate(providerNamespace, type);
        ValidateVersion(version);
        return DeleteVersionAndArtifactsAsync(providerNamespace, type, version);
    }

    public async Task<ProviderPlatform> CreatePlatformAsync(string providerNamespace, string type, string version,
        CreateProviderPlatformRequest request)
    {
        var providerVersion = await RequireProviderVersionAsync(providerNamespace, type, version);
        ValidateProviderSegment(request.Os, "provider platform os");
        ValidateProviderSegment(request.Arch, "provider platform architecture");
        ValidateSha256(request.Shasum);

        var expectedFilename = ProviderPackageValidator.ExpectedProviderPackageFilename(type, version, request.Os, request.Arch);
        if (!string.Equals(request.Filename, expectedFilename, StringComparison.Ordinal))
            throw new ArgumentException($"Provider package filename must be {expectedFilename}.", nameof(request));

        return await _repository.CreateProviderPlatformAsync(
            providerVersion.Id,
            request.Os,
            request.Arch,
            request.Filename,
            request.Shasum);
    }

    public async Task<bool> UploadPlatformPackageAsync(string providerNamespace, string type, string version, string os, string arch,
        Stream package, CancellationToken cancellationToken)
    {
        ValidateCoordinate(providerNamespace, type);
        var providerVersion = await _repository.GetProviderVersionAsync(providerNamespace, type, version);
        if (providerVersion == null ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsStoragePath) ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsSignatureStoragePath))
        {
            return false;
        }

        var platform = await _repository.GetProviderPlatformAsync(providerNamespace, type, version, os, arch);
        if (platform == null)
        {
            return false;
        }

        var gpgKey = await _repository.GetGpgKeyAsync(providerNamespace, providerVersion.KeyId);
        if (gpgKey == null)
        {
            return false;
        }

        await using var packageBuffer = await CopyToReplayableFileAsync(package, cancellationToken);
        await using var shasums = await _storage.OpenReadAsync(providerVersion.ShasumsStoragePath, cancellationToken);
        if (shasums == null)
        {
            return false;
        }

        await using var signature = await _storage.OpenReadAsync(providerVersion.ShasumsSignatureStoragePath, cancellationToken);
        if (signature == null)
        {
            return false;
        }

        var validation = await _validator.ValidatePackageAsync(type, version, os, arch, platform.Filename, platform.Shasum,
            packageBuffer, shasums, signature, gpgKey.AsciiArmor, cancellationToken);
        if (!validation.Valid)
        {
            RegistryLog.Warning(_logger, "Provider package validation failed for {Namespace}/{Type}/{Version}/{Os}/{Arch}: {Error}",
                providerNamespace, type, version, os, arch, validation.Error);
            return false;
        }

        packageBuffer.Position = 0;
        var saveResult = await _storage.SaveAsync(PackagePath(providerNamespace, type, version, os, arch, platform.Filename), packageBuffer, cancellationToken);
        return await _repository.SetPlatformPackagePathAsync(platform.Id, saveResult.StoragePath, saveResult.SizeBytes);
    }

    public Task<bool> DeletePlatformAsync(string providerNamespace, string type, string version, string os, string arch)
    {
        ValidateCoordinate(providerNamespace, type);
        ValidateVersion(version);
        ValidateProviderSegment(os, "provider platform os");
        ValidateProviderSegment(arch, "provider platform architecture");
        return DeletePlatformAndArtifactsAsync(providerNamespace, type, version, os, arch);
    }

    private async Task<FileStream> CopyToReplayableFileAsync(Stream source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_uploadOptions.TempRoot);
        var path = Path.Join(_uploadOptions.TempRoot, $".{Guid.NewGuid():N}.provider-upload");
        try
        {
            var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            var buffer = new byte[1024 * 1024];
            long copied = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                copied = checked(copied + read);
                if (copied > _uploadOptions.MaxPackageBytes)
                {
                    await output.DisposeAsync();
                    throw new InvalidOperationException($"Provider package exceeds the configured limit of {_uploadOptions.MaxPackageBytes} bytes.");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private async Task<bool> DeleteProviderAndArtifactsAsync(string providerNamespace, string type)
    {
        var paths = await _repository.GetProviderArtifactStoragePathsAsync(providerNamespace, type, null, null, null);
        var deleted = await _repository.DeleteProviderAsync(providerNamespace, type);
        if (deleted)
        {
            await DeleteArtifactsAsync(paths);
        }

        return deleted;
    }

    private async Task<bool> DeleteVersionAndArtifactsAsync(string providerNamespace, string type, string version)
    {
        var paths = await _repository.GetProviderArtifactStoragePathsAsync(providerNamespace, type, version, null, null);
        var deleted = await _repository.DeleteProviderVersionAsync(providerNamespace, type, version);
        if (deleted)
        {
            await DeleteArtifactsAsync(paths);
        }

        return deleted;
    }

    private async Task<bool> DeletePlatformAndArtifactsAsync(string providerNamespace, string type, string version, string os, string arch)
    {
        var paths = await _repository.GetProviderArtifactStoragePathsAsync(providerNamespace, type, version, os, arch);
        var deleted = await _repository.DeleteProviderPlatformAsync(providerNamespace, type, version, os, arch);
        if (deleted)
        {
            await DeleteArtifactsAsync(paths);
        }

        return deleted;
    }

    private async Task DeleteArtifactsAsync(IReadOnlyList<string> storagePaths)
    {
        foreach (var storagePath in storagePaths.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await _storage.DeleteAsync(storagePath, CancellationToken.None);
            }
            catch (Exception ex)
            {
                RegistryLog.Warning(_logger, ex, "Failed to delete provider artifact {StoragePath}", storagePath);
            }
        }
    }

    private async Task<ProviderVersion> RequireProviderVersionAsync(string providerNamespace, string type, string version)
    {
        ValidateCoordinate(providerNamespace, type);
        ValidateVersion(version);
        return await _repository.GetProviderVersionAsync(providerNamespace, type, version) ??
               throw new InvalidOperationException($"Provider version {providerNamespace}/{type}/{version} was not found.");
    }

    private static void ValidateCoordinate(string providerNamespace, string type)
    {
        var coordinateError = ProviderIdentifierValidator.GetProviderCoordinateError(providerNamespace, type);
        if (coordinateError != null)
            throw new ArgumentException(coordinateError);
    }

    private static void ValidateProviderSegment(string value, string label)
    {
        if (!ProviderIdentifierValidator.IsValidProviderSegment(value))
        {
            throw new ArgumentException($"Invalid {label}. Use lowercase letters, numbers, or hyphens; start with a letter or number.");
        }
    }

    private static void ValidateVersion(string version)
    {
        if (!SemVerValidator.IsValid(version))
        {
            throw new ArgumentException(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]",
                nameof(version));
        }
    }

    private static void ValidateProtocols(string[] protocols)
    {
        if (protocols.Length == 0)
            throw new ArgumentException("At least one provider protocol is required.", nameof(protocols));

        foreach (var protocol in protocols)
        {
            if (!TryParseProviderProtocol(protocol, out var major, out _) || !SupportedProtocolMajors.Contains(major))
            {
                throw new ArgumentException(
                    $"Unsupported provider protocol '{protocol}'. Supported provider protocol majors are 5 and 6 in MAJOR.MINOR format.",
                    nameof(protocols));
            }
        }
    }

    private static bool TryParseProviderProtocol(string protocol, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        var parts = protocol.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor) &&
               major >= 0 &&
               minor >= 0 &&
               string.Equals(protocol, $"{major}.{minor}", StringComparison.Ordinal);
    }

    private static void ValidateSha256(string shasum)
    {
        if (shasum.Length != 64 || shasum.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Provider platform shasum must be a SHA256 hex digest.", nameof(shasum));
    }

    private static string ProviderBasePath(string providerNamespace, string type, string version) =>
        $"{providerNamespace}/{type}/{version}";

    private static string ShasumsPath(string providerNamespace, string type, string version) =>
        $"{ProviderBasePath(providerNamespace, type, version)}/terraform-provider-{type}_{version}_SHA256SUMS";

    private static string SignaturePath(string providerNamespace, string type, string version) =>
        $"{ProviderBasePath(providerNamespace, type, version)}/terraform-provider-{type}_{version}_SHA256SUMS.sig";

    private static string PackagePath(string providerNamespace, string type, string version, string os, string arch, string filename) =>
        $"{ProviderBasePath(providerNamespace, type, version)}/{os}_{arch}/{filename}";
}
