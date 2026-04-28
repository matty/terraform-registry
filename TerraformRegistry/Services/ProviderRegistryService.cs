using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class ProviderRegistryService : IProviderRegistryService
{
    private static readonly HashSet<string> AllowedProtocols = new(StringComparer.Ordinal) { "5.0", "5.1", "6.0" };
    private readonly ILogger<ProviderRegistryService> _logger;
    private readonly IProviderRepository _repository;
    private readonly IProviderArtifactStorage _storage;
    private readonly IProviderPackageValidator _validator;

    public ProviderRegistryService(
        IProviderRepository repository,
        IProviderArtifactStorage storage,
        IProviderPackageValidator validator,
        ILogger<ProviderRegistryService> logger)
    {
        _repository = repository;
        _storage = storage;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ProviderVersionsResponse?> GetVersionsAsync(string @namespace, string type)
    {
        ValidateCoordinate(@namespace, type);

        var versions = await _repository.GetProviderVersionsAsync(@namespace, type);
        if (versions.Count == 0)
        {
            return null;
        }

        return new ProviderVersionsResponse { Versions = versions.ToList() };
    }

    public async Task<ProviderManagementVersionsResponse?> GetManagementVersionsAsync(string @namespace, string type)
    {
        ValidateCoordinate(@namespace, type);

        var provider = await _repository.GetProviderAsync(@namespace, type);
        if (provider == null)
        {
            return null;
        }

        var versions = await _repository.GetProviderManagementVersionsAsync(@namespace, type);
        return new ProviderManagementVersionsResponse { Versions = versions.ToList() };
    }

    public async Task<ProviderManagementPlatformsResponse?> GetManagementPlatformsAsync(string @namespace, string type, string version)
    {
        ValidateCoordinate(@namespace, type);
        ValidateVersion(version);

        var providerVersion = await _repository.GetProviderVersionAsync(@namespace, type, version);
        if (providerVersion == null)
        {
            return null;
        }

        var platforms = await _repository.GetProviderManagementPlatformsAsync(@namespace, type, version);
        return new ProviderManagementPlatformsResponse { Platforms = platforms.ToList() };
    }

    public async Task<ProviderPackageResponse?> GetPackageAsync(string @namespace, string type, string version, string os,
        string arch, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        ValidateCoordinate(@namespace, type);

        var provider = await _repository.GetProviderAsync(@namespace, type);
        if (provider == null)
        {
            return null;
        }

        var providerVersion = await _repository.GetProviderVersionAsync(@namespace, type, version);
        if (providerVersion == null ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsStoragePath) ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsSignatureStoragePath))
        {
            return null;
        }

        var platform = await _repository.GetProviderPlatformAsync(@namespace, type, version, os, arch);
        if (platform == null || string.IsNullOrWhiteSpace(platform.PackageStoragePath))
        {
            return null;
        }

        var gpgKey = await _repository.GetGpgKeyAsync(@namespace, providerVersion.KeyId);
        if (gpgKey == null)
        {
            return null;
        }

        try
        {
            var packageUrl = await _storage.CreateDownloadUrlAsync(platform.PackageStoragePath, cancellationToken);
            var shasumsUrl = await _storage.CreateDownloadUrlAsync(providerVersion.ShasumsStoragePath, cancellationToken);
            var signatureUrl = await _storage.CreateDownloadUrlAsync(providerVersion.ShasumsSignatureStoragePath, cancellationToken);

            await _repository.RecordProviderDownloadAsync(provider.Id, @namespace, type, version, os, arch, clientIp, userAgent);

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
            _logger.LogWarning(ex, "Provider package artifact metadata is incomplete for {Namespace}/{Type}/{Version}/{Os}/{Arch}",
                @namespace, type, version, os, arch);
            return null;
        }
    }

    public Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit)
    {
        return _repository.ListProvidersAsync(q, Math.Max(0, offset), Math.Clamp(limit, 1, 100));
    }

    public Task<TerraformProvider?> GetProviderAsync(string @namespace, string type)
    {
        ValidateCoordinate(@namespace, type);
        return _repository.GetProviderAsync(@namespace, type);
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

    public Task<bool> UpdateProviderAsync(string @namespace, string type, UpdateProviderRequest request)
    {
        ValidateCoordinate(@namespace, type);
        return _repository.UpdateProviderAsync(@namespace, type, request.DisplayName, request.Description, request.SourceRepositoryUrl);
    }

    public Task<bool> DeleteProviderAsync(string @namespace, string type)
    {
        ValidateCoordinate(@namespace, type);
        return _repository.DeleteProviderAsync(@namespace, type);
    }

    public Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string @namespace)
    {
        ValidateProviderSegment(@namespace, "provider namespace");
        return _repository.ListGpgKeysAsync(@namespace);
    }

    public Task<ProviderGpgKey> AddGpgKeyAsync(string @namespace, CreateProviderGpgKeyRequest request)
    {
        ValidateProviderSegment(@namespace, "provider namespace");
        if (string.IsNullOrWhiteSpace(request.KeyId))
            throw new ArgumentException("Provider GPG key ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AsciiArmor))
            throw new ArgumentException("Provider GPG public key is required.", nameof(request));

        return _repository.AddGpgKeyAsync(new ProviderGpgKey
        {
            Namespace = @namespace,
            KeyId = request.KeyId,
            AsciiArmor = request.AsciiArmor,
            TrustSignature = request.TrustSignature,
            Source = request.Source,
            SourceUrl = request.SourceUrl,
            CreatedAt = DateTime.UtcNow
        });
    }

    public Task<bool> RevokeGpgKeyAsync(string @namespace, string keyId)
    {
        ValidateProviderSegment(@namespace, "provider namespace");
        return _repository.RevokeGpgKeyAsync(@namespace, keyId);
    }

    public async Task<ProviderVersion> CreateVersionAsync(string @namespace, string type, CreateProviderVersionRequest request)
    {
        ValidateCoordinate(@namespace, type);
        ValidateVersion(request.Version);
        ValidateProtocols(request.Protocols);

        var provider = await _repository.GetProviderAsync(@namespace, type) ??
                       throw new InvalidOperationException($"Provider {@namespace}/{type} was not found.");
        _ = await _repository.GetGpgKeyAsync(@namespace, request.KeyId) ??
            throw new InvalidOperationException($"Provider GPG key {request.KeyId} was not found.");

        return await _repository.CreateProviderVersionAsync(provider.Id, request.Version, request.Protocols, request.KeyId);
    }

    public async Task<bool> UploadShasumsAsync(string @namespace, string type, string version, Stream content,
        CancellationToken cancellationToken)
    {
        var providerVersion = await RequireProviderVersionAsync(@namespace, type, version);
        var saveResult = await _storage.SaveAsync(ShasumsPath(@namespace, type, version), content, cancellationToken);
        return await _repository.SetVersionShasumsPathAsync(providerVersion.Id, saveResult.StoragePath);
    }

    public async Task<bool> UploadShasumsSignatureAsync(string @namespace, string type, string version, Stream content,
        CancellationToken cancellationToken)
    {
        var providerVersion = await RequireProviderVersionAsync(@namespace, type, version);
        var saveResult = await _storage.SaveAsync(SignaturePath(@namespace, type, version), content, cancellationToken);
        return await _repository.SetVersionShasumsSignaturePathAsync(providerVersion.Id, saveResult.StoragePath);
    }

    public Task<bool> DeleteVersionAsync(string @namespace, string type, string version)
    {
        ValidateCoordinate(@namespace, type);
        return _repository.DeleteProviderVersionAsync(@namespace, type, version);
    }

    public async Task<ProviderPlatform> CreatePlatformAsync(string @namespace, string type, string version,
        CreateProviderPlatformRequest request)
    {
        var providerVersion = await RequireProviderVersionAsync(@namespace, type, version);
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

    public async Task<bool> UploadPlatformPackageAsync(string @namespace, string type, string version, string os, string arch,
        Stream package, CancellationToken cancellationToken)
    {
        ValidateCoordinate(@namespace, type);
        var providerVersion = await _repository.GetProviderVersionAsync(@namespace, type, version);
        if (providerVersion == null ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsStoragePath) ||
            string.IsNullOrWhiteSpace(providerVersion.ShasumsSignatureStoragePath))
        {
            return false;
        }

        var platform = await _repository.GetProviderPlatformAsync(@namespace, type, version, os, arch);
        if (platform == null)
        {
            return false;
        }

        var gpgKey = await _repository.GetGpgKeyAsync(@namespace, providerVersion.KeyId);
        if (gpgKey == null)
        {
            return false;
        }

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
            package, shasums, signature, gpgKey.AsciiArmor, cancellationToken);
        if (!validation.Valid)
        {
            _logger.LogWarning("Provider package validation failed for {Namespace}/{Type}/{Version}/{Os}/{Arch}: {Error}",
                @namespace, type, version, os, arch, validation.Error);
            return false;
        }

        if (package.CanSeek) package.Position = 0;
        var saveResult = await _storage.SaveAsync(PackagePath(@namespace, type, version, os, arch, platform.Filename), package, cancellationToken);
        return await _repository.SetPlatformPackagePathAsync(platform.Id, saveResult.StoragePath, saveResult.SizeBytes);
    }

    public Task<bool> DeletePlatformAsync(string @namespace, string type, string version, string os, string arch)
    {
        ValidateCoordinate(@namespace, type);
        return _repository.DeleteProviderPlatformAsync(@namespace, type, version, os, arch);
    }

    private async Task<ProviderVersion> RequireProviderVersionAsync(string @namespace, string type, string version)
    {
        ValidateCoordinate(@namespace, type);
        ValidateVersion(version);
        return await _repository.GetProviderVersionAsync(@namespace, type, version) ??
               throw new InvalidOperationException($"Provider version {@namespace}/{type}/{version} was not found.");
    }

    private static void ValidateCoordinate(string @namespace, string type)
    {
        var coordinateError = ProviderIdentifierValidator.GetProviderCoordinateError(@namespace, type);
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

        var unsupported = protocols.FirstOrDefault(protocol => !AllowedProtocols.Contains(protocol));
        if (unsupported != null)
            throw new ArgumentException($"Unsupported provider protocol '{unsupported}'. Supported protocols: 5.0, 5.1, 6.0.", nameof(protocols));
    }

    private static void ValidateSha256(string shasum)
    {
        if (shasum.Length != 64 || shasum.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Provider platform shasum must be a SHA256 hex digest.", nameof(shasum));
    }

    private static string ProviderBasePath(string @namespace, string type, string version) =>
        $"{@namespace}/{type}/{version}";

    private static string ShasumsPath(string @namespace, string type, string version) =>
        $"{ProviderBasePath(@namespace, type, version)}/terraform-provider-{type}_{version}_SHA256SUMS";

    private static string SignaturePath(string @namespace, string type, string version) =>
        $"{ProviderBasePath(@namespace, type, version)}/terraform-provider-{type}_{version}_SHA256SUMS.sig";

    private static string PackagePath(string @namespace, string type, string version, string os, string arch, string filename) =>
        $"{ProviderBasePath(@namespace, type, version)}/{os}_{arch}/{filename}";
}
