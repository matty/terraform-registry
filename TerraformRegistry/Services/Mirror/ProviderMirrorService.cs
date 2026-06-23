using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Services.Mirror;

public sealed class ProviderMirrorService(
    IProviderMirrorRepository repository,
    IProviderArtifactStorage storage,
    IMirrorPolicyService policyService,
    IMirrorConfigService configService,
    IMirrorLeaseService leaseService,
    IHttpClientFactory httpClientFactory,
    MirrorPackageUrlSigner signer,
    ILogger<ProviderMirrorService> logger) : IProviderMirrorService
{
    private const string MirrorHttpClientName = "TerraformRegistryMirror";
    private const int BufferSize = 81920;
    private static readonly TimeSpan LeaseReleaseTimeout = TimeSpan.FromSeconds(5);
    private const int SignedPackageUrlLifetimeMinutes = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object EmptyVersion = new();

    public async Task<ProviderMirrorIndexResponse?> GetProviderIndexAsync(
        string hostname,
        string providerNamespace,
        string type,
        CancellationToken cancellationToken)
    {
        var config = (await configService.GetConfigAsync(cancellationToken)).Effective;
        if (!config.Enabled || !config.Providers.Enabled)
        {
            return null;
        }

        if (!await policyService.IsProviderAllowedAsync(hostname, providerNamespace, type, string.Empty, string.Empty, cancellationToken))
        {
            return null;
        }

        var versions = await GetProviderVersionsAsync(hostname, providerNamespace, type, config, cancellationToken);
        return versions is null ? null : ToNetworkMirrorIndex(versions);
    }

    public async Task<ProviderMirrorVersionResponse?> GetProviderVersionAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        var config = (await configService.GetConfigAsync(cancellationToken)).Effective;
        if (!config.Enabled || !config.Providers.Enabled)
        {
            return null;
        }

        if (!await policyService.IsProviderAllowedAsync(hostname, providerNamespace, type, string.Empty, string.Empty, cancellationToken))
        {
            return null;
        }

        var versions = await GetProviderVersionsAsync(hostname, providerNamespace, type, config, cancellationToken);
        var selected = versions?.Versions.FirstOrDefault(x => string.Equals(x.Version, version, StringComparison.Ordinal));
        if (selected is null)
        {
            return null;
        }

        var archives = new Dictionary<string, ProviderMirrorArchive>(StringComparer.Ordinal);
        foreach (var platform in selected.Platforms)
        {
            if (!await policyService.IsProviderAllowedAsync(hostname, providerNamespace, type, platform.Os, platform.Arch, cancellationToken))
            {
                continue;
            }

            var package = await GetOrFetchPackageAsync(
                hostname,
                providerNamespace,
                type,
                selected,
                platform,
                config,
                cancellationToken);
            if (package is null || string.IsNullOrWhiteSpace(package.Filename))
            {
                continue;
            }

            var hashes = DeserializeStringArray(package.HashesJson);
            if (hashes.Length == 0)
            {
                continue;
            }

            archives[$"{platform.Os}_{platform.Arch}"] = new ProviderMirrorArchive
            {
                Url = signer.CreateSignedPackageUrl(
                    hostname,
                    providerNamespace,
                    type,
                    version,
                    platform.Os,
                    platform.Arch,
                    package.Filename,
                    DateTimeOffset.UtcNow.AddMinutes(SignedPackageUrlLifetimeMinutes)),
                Hashes = hashes
            };
        }

        return archives.Count == 0 ? null : new ProviderMirrorVersionResponse { Archives = archives };
    }

    public async Task<ProviderMirrorPackageDownload?> OpenPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        string filename,
        IReadOnlyDictionary<string, string[]> query,
        CancellationToken cancellationToken)
    {
        var signedUrl = BuildSignedUrl(hostname, providerNamespace, type, filename, query);
        if (!signer.TryValidate(signedUrl, DateTimeOffset.UtcNow, out var claims) ||
            !string.Equals(claims.Hostname, hostname, StringComparison.Ordinal) ||
            !string.Equals(claims.Namespace, providerNamespace, StringComparison.Ordinal) ||
            !string.Equals(claims.Type, type, StringComparison.Ordinal) ||
            !string.Equals(claims.Filename, filename, StringComparison.Ordinal))
        {
            return null;
        }

        var package = await repository.GetProviderPackageAsync(
            claims.Hostname,
            claims.Namespace,
            claims.Type,
            claims.Version,
            claims.Os,
            claims.Arch);

        if (package is null ||
            !string.Equals(package.State, "ready", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.Filename, claims.Filename, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(package.PackageStoragePath))
        {
            return null;
        }

        var content = await storage.OpenReadAsync(package.PackageStoragePath, cancellationToken);
        if (content is null)
        {
            await repository.MarkProviderPackageFailedAsync(
                claims.Hostname,
                claims.Namespace,
                claims.Type,
                claims.Version,
                claims.Os,
                claims.Arch,
                "Cached provider package artifact is missing.");
            return null;
        }

        return new ProviderMirrorPackageDownload(
            content,
            package.Filename ?? filename,
            "application/zip",
            package.SizeBytes);
    }

    private async Task<UpstreamProviderVersions?> GetProviderVersionsAsync(
        string hostname,
        string providerNamespace,
        string type,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        var cached = await repository.GetProviderIndexAsync(hostname, providerNamespace, type);
        if (cached is not null &&
            string.Equals(cached.State, "ready", StringComparison.OrdinalIgnoreCase) &&
            cached.LastSyncAt is { } lastSync &&
            lastSync.AddMinutes(Math.Max(1, config.Providers.MetadataTtlMinutes)) > DateTime.UtcNow)
        {
            return DeserializeVersions(cached.VersionsJson);
        }

        var client = httpClientFactory.CreateClient();
        var uri = BuildUpstreamUri(config.UpstreamRegistryBaseUrl, $"/v1/providers/{providerNamespace}/{type}/versions");
        using var response = await client.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var versions = DeserializeVersions(json);
        if (versions is null)
        {
            return null;
        }

        await repository.UpsertProviderIndexAsync(new MirrorProviderIndex
        {
            Hostname = hostname,
            Namespace = providerNamespace,
            Type = type,
            VersionsJson = json,
            ETag = response.Headers.ETag?.Tag,
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        });

        return versions;
    }

    private async Task<MirrorProviderPackage?> GetOrFetchPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        UpstreamProviderVersion version,
        UpstreamProviderPlatform platform,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        var cached = await GetReadyPackageAsync(hostname, providerNamespace, type, version.Version, platform.Os, platform.Arch, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var leaseKey = $"provider-package:{hostname}:{providerNamespace}:{type}:{version.Version}:{platform.Os}:{platform.Arch}";
        var lease = await leaseService.TryAcquireAsync(leaseKey, "provider-package", TimeSpan.FromMinutes(5), cancellationToken);
        if (lease is null)
        {
            return await GetReadyPackageAsync(hostname, providerNamespace, type, version.Version, platform.Os, platform.Arch, cancellationToken);
        }

        try
        {
            cached = await GetReadyPackageAsync(hostname, providerNamespace, type, version.Version, platform.Os, platform.Arch, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            return await FetchAndCachePackageAsync(hostname, providerNamespace, type, version, platform, config, cancellationToken);
        }
        finally
        {
            await ReleaseLeaseAsync(lease);
        }
    }

    private async Task ReleaseLeaseAsync(MirrorLeaseHandle lease)
    {
        try
        {
            using var releaseCts = new CancellationTokenSource(LeaseReleaseTimeout);
            await leaseService.ReleaseAsync(lease, releaseCts.Token);
        }
        catch (Exception ex)
        {
            RegistryLog.Warning(
                logger,
                ex,
                "Failed to release provider mirror lease {LeaseKey} held by {OwnerInstanceId}",
                lease.LeaseKey,
                lease.OwnerInstanceId);
        }
    }

    private async Task<MirrorProviderPackage?> GetReadyPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        CancellationToken cancellationToken)
    {
        var cached = await repository.GetProviderPackageAsync(hostname, providerNamespace, type, version, os, arch);
        if (cached is null ||
            !string.Equals(cached.State, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cached.PackageStoragePath))
        {
            return null;
        }

        if (await storage.ExistsAsync(cached.PackageStoragePath, cancellationToken))
        {
            return cached;
        }

        await repository.MarkProviderPackageFailedAsync(
            hostname,
            providerNamespace,
            type,
            version,
            os,
            arch,
            "Cached provider package artifact is missing.");
        return null;
    }

    private async Task<MirrorProviderPackage?> FetchAndCachePackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        UpstreamProviderVersion version,
        UpstreamProviderPlatform platform,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        try
        {
            var metadataUri = BuildUpstreamUri(
                config.UpstreamRegistryBaseUrl,
                $"/v1/providers/{providerNamespace}/{type}/{version.Version}/download/{platform.Os}/{platform.Arch}");
            var metadata = await client.GetFromJsonAsync<ProviderPackageResponse>(metadataUri, JsonOptions, cancellationToken);
            if (metadata is null)
            {
                return null;
            }

            ValidateUpstreamPackageMetadata(type, version.Version, platform, metadata);

            await using var packageArtifact = await DownloadArtifactToTempFileAsync(
                metadata.DownloadUrl,
                config.Providers.MaxPackageBytes,
                config.Providers.MaxRedirects,
                computeSha256: true,
                cancellationToken);
            var packageSha = packageArtifact.Sha256Hex ??
                             throw new InvalidOperationException("Provider package SHA-256 could not be computed.");
            if (!string.Equals(packageSha, metadata.Shasum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider package SHA-256 did not match upstream shasum.");
            }

            await using var shasumsArtifact = await DownloadArtifactToTempFileAsync(
                metadata.ShasumsUrl,
                config.Providers.MaxChecksumBytes,
                config.Providers.MaxRedirects,
                computeSha256: false,
                cancellationToken);
            shasumsArtifact.Content.Position = 0;
            using var shasumsReader = new StreamReader(shasumsArtifact.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var shasumsText = await shasumsReader.ReadToEndAsync(cancellationToken);
            var shasumsEntry = FindShasumsEntry(shasumsText, metadata.Filename);
            if (!string.Equals(shasumsEntry, metadata.Shasum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider package SHA-256 did not match upstream SHA256SUMS entry.");
            }

            await using var signatureArtifact = await DownloadArtifactToTempFileAsync(
                metadata.ShasumsSignatureUrl,
                config.Providers.MaxChecksumBytes,
                config.Providers.MaxRedirects,
                computeSha256: false,
                cancellationToken);
            var packagePath = PackageStoragePath(hostname, providerNamespace, type, version.Version, platform.Os, platform.Arch, metadata.Filename);
            var shasumsPath = ShasumsStoragePath(hostname, providerNamespace, type, version.Version);
            var signaturePath = $"{shasumsPath}.sig";

            packageArtifact.Content.Position = 0;
            var packageSave = await storage.SaveAsync(packagePath, packageArtifact.Content, cancellationToken);
            shasumsArtifact.Content.Position = 0;
            var shasumsSave = await storage.SaveAsync(shasumsPath, shasumsArtifact.Content, cancellationToken);
            signatureArtifact.Content.Position = 0;
            var signatureSave = await storage.SaveAsync(signaturePath, signatureArtifact.Content, cancellationToken);

            var hashes = new[] { $"zh:{packageSha}" };
            var package = new MirrorProviderPackage
            {
                Hostname = hostname,
                Namespace = providerNamespace,
                Type = type,
                Version = version.Version,
                Os = platform.Os,
                Arch = platform.Arch,
                DownloadUrl = metadata.DownloadUrl,
                Filename = metadata.Filename,
                PackageStoragePath = packageSave.StoragePath,
                SizeBytes = packageSave.SizeBytes,
                ProtocolsJson = JsonSerializer.Serialize(metadata.Protocols, JsonOptions),
                HashesJson = JsonSerializer.Serialize(hashes, JsonOptions),
                Shasum = packageSha,
                SigningKeysJson = JsonSerializer.Serialize(new
                {
                    signing_keys = metadata.SigningKeys,
                    signature_verification = "not_verified",
                    shasums_storage_path = shasumsSave.StoragePath,
                    shasums_signature_storage_path = signatureSave.StoragePath
                }, JsonOptions),
                State = "ready",
                LastSyncAt = DateTime.UtcNow
            };

            await repository.UpsertProviderPackageAsync(package);
            return package;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or IOException)
        {
            RegistryLog.Warning(
                logger,
                ex,
                "Provider mirror fetch failed for {Hostname}/{Namespace}/{Type}/{Version}/{Os}/{Arch}",
                hostname,
                providerNamespace,
                type,
                version.Version,
                platform.Os,
                platform.Arch);
            await repository.MarkProviderPackageFailedAsync(
                hostname,
                providerNamespace,
                type,
                version.Version,
                platform.Os,
                platform.Arch,
                ex.Message,
                ex is HttpRequestException httpEx ? (int?)httpEx.StatusCode : null);
            return null;
        }
    }

    private static void ValidateUpstreamPackageMetadata(
        string type,
        string version,
        UpstreamProviderPlatform requestedPlatform,
        ProviderPackageResponse metadata)
    {
        if (!string.Equals(metadata.Os, requestedPlatform.Os, StringComparison.Ordinal) ||
            !string.Equals(metadata.Arch, requestedPlatform.Arch, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider package metadata platform did not match requested platform.");
        }

        var expectedFilename = ProviderPackageValidator.ExpectedProviderPackageFilename(
            type,
            version,
            requestedPlatform.Os,
            requestedPlatform.Arch);
        if (!string.Equals(metadata.Filename, expectedFilename, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Provider package filename must be {expectedFilename}.");
        }

        if (metadata.Filename.Contains('/', StringComparison.Ordinal) ||
            metadata.Filename.Contains('\\', StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(metadata.Filename), metadata.Filename, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider package filename must not contain path separators.");
        }
    }

    private async Task<TempDownloadedArtifact> DownloadArtifactToTempFileAsync(
        string url,
        long maxBytes,
        int maxRedirects,
        bool computeSha256,
        CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum byte count must be positive.");
        }

        if (maxRedirects < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRedirects), "Maximum redirects must not be negative.");
        }

        var currentEndpoint = await policyService.ValidateProviderArtifactUrlAsync(url, cancellationToken);
        var mirrorClient = httpClientFactory.CreateClient(MirrorHttpClientName);

        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentEndpoint.Uri);
            MirrorPinnedConnectionHelper.AttachValidatedAddresses(request, currentEndpoint.Addresses);
            using var response = await mirrorClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= maxRedirects)
                {
                    throw new InvalidOperationException("Provider mirror artifact fetch exceeded the maximum redirect count.");
                }

                if (response.Headers.Location is null)
                {
                    throw new InvalidOperationException("Provider mirror artifact fetch redirect did not include a Location header.");
                }

                var redirectUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(currentEndpoint.Uri, response.Headers.Location);
                currentEndpoint = await policyService.ValidateProviderArtifactUrlAsync(redirectUri.ToString(), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await ReadArtifactToTempFileAsync(response, currentEndpoint.Uri, maxBytes, computeSha256, cancellationToken);
        }
    }

    private static async Task<TempDownloadedArtifact> ReadArtifactToTempFileAsync(
        HttpResponseMessage response,
        Uri currentUri,
        long maxBytes,
        bool computeSha256,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > maxBytes)
        {
            throw new InvalidOperationException("Provider mirror artifact response exceeded the maximum byte count.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"terraform-provider-mirror-{Guid.NewGuid():N}.tmp");
        FileStream? destination = null;
        IncrementalHash? hash = null;
        try
        {
            destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            hash = computeSha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[BufferSize];
            long totalBytes = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maxBytes)
                {
                    throw new InvalidOperationException("Provider mirror artifact response exceeded the maximum byte count.");
                }

                hash?.AppendData(buffer.AsSpan(0, read));
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            destination.Position = 0;
            var sha256 = hash is null ? null : Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new TempDownloadedArtifact(destination, totalBytes, sha256);
        }
        catch
        {
            hash?.Dispose();
            if (destination is not null)
            {
                await destination.DisposeAsync();
            }
            else if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
        finally
        {
            hash?.Dispose();
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static ProviderMirrorIndexResponse ToNetworkMirrorIndex(UpstreamProviderVersions versions)
    {
        var indexVersions = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var version in versions.Versions)
        {
            indexVersions[version.Version] = EmptyVersion;
        }

        return new ProviderMirrorIndexResponse { Versions = indexVersions };
    }

    private static UpstreamProviderVersions? DeserializeVersions(string json)
    {
        return JsonSerializer.Deserialize<UpstreamProviderVersions>(json, JsonOptions);
    }

    private static string[] DeserializeStringArray(string json)
    {
        return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
    }

    private static Uri BuildUpstreamUri(string upstreamBaseUrl, string path)
    {
        var baseUri = new Uri(upstreamBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private static string PackageStoragePath(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        string filename)
    {
        return $"mirror/providers/{hostname}/{providerNamespace}/{type}/{version}/{os}_{arch}/{filename}";
    }

    private static string ShasumsStoragePath(
        string hostname,
        string providerNamespace,
        string type,
        string version)
    {
        return $"mirror/providers/{hostname}/{providerNamespace}/{type}/{version}/terraform-provider-{type}_{version}_SHA256SUMS";
    }

    private static string? FindShasumsEntry(string shasumsText, string filename)
    {
        foreach (var line in shasumsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var listedFilename = parts[^1].TrimStart('*');
            if (string.Equals(listedFilename, filename, StringComparison.Ordinal))
            {
                return parts[0];
            }
        }

        return null;
    }

    private static string BuildSignedUrl(
        string hostname,
        string providerNamespace,
        string type,
        string filename,
        IReadOnlyDictionary<string, string[]> query)
    {
        var path = $"/mirror/providers/{Uri.EscapeDataString(hostname)}/{Uri.EscapeDataString(providerNamespace)}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(filename)}";
        var pairs = query.SelectMany(x => x.Value.Select(value => new KeyValuePair<string, string?>(x.Key, value)));
        return path + QueryString.Create(pairs);
    }

    private sealed class UpstreamProviderVersions
    {
        public List<UpstreamProviderVersion> Versions { get; init; } = [];
    }

    private sealed class UpstreamProviderVersion
    {
        public string Version { get; init; } = string.Empty;
        public string[] Protocols { get; init; } = [];
        public List<UpstreamProviderPlatform> Platforms { get; init; } = [];
    }

    private sealed class UpstreamProviderPlatform
    {
        public string Os { get; init; } = string.Empty;
        public string Arch { get; init; } = string.Empty;
    }

    private sealed class TempDownloadedArtifact(FileStream content, long length, string? sha256Hex) : IAsyncDisposable
    {
        public FileStream Content { get; } = content;
        public long Length { get; } = length;
        public string? Sha256Hex { get; } = sha256Hex;

        public ValueTask DisposeAsync() => Content.DisposeAsync();
    }
}
