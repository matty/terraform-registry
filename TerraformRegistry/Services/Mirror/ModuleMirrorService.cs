using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Publishing;

namespace TerraformRegistry.Services.Mirror;

public sealed class ModuleMirrorService(
    IModuleService moduleService,
    IModuleMirrorRepository repository,
    IMirrorPolicyService policyService,
    IMirrorConfigService configService,
    IMirrorLeaseService leaseService,
    IHttpClientFactory httpClientFactory,
    MirrorHttpClient mirrorHttpClient,
    IModulePublishCoordinator publishCoordinator,
    ILogger<ModuleMirrorService> logger,
    MirrorDownloadAdmission? downloadAdmission = null) : IModuleMirrorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseReleaseTimeout = TimeSpan.FromSeconds(5);
    private const string MirrorDiscoveryHttpClientName = "TerraformRegistryMirrorDiscovery";
    private readonly MirrorDownloadAdmission _downloadAdmission = downloadAdmission ?? new MirrorDownloadAdmission();

    public async Task<ModuleVersions> GetModuleVersionsAsync(
        string moduleNamespace,
        string name,
        string provider,
        ModuleVersions localVersions,
        CancellationToken cancellationToken = default)
    {
        var config = (await configService.GetConfigAsync(cancellationToken)).Effective;
        var hostname = GetUpstreamHostname(config);
        if (!await IsMirrorAllowedAsync(hostname, moduleNamespace, name, provider, config, cancellationToken))
        {
            return localVersions;
        }

        var upstreamVersions = await GetUpstreamVersionsAsync(
            hostname,
            moduleNamespace,
            name,
            provider,
            config,
            cancellationToken);

        return upstreamVersions is null ? localVersions : MergeVersions(localVersions, upstreamVersions);
    }

    public async Task<TerraformModule?> GetModuleAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        TerraformModule? localModule,
        CancellationToken cancellationToken = default)
    {
        if (localModule is not null)
        {
            return localModule;
        }

        var config = (await configService.GetConfigAsync(cancellationToken)).Effective;
        var hostname = GetUpstreamHostname(config);
        if (!await IsMirrorAllowedAsync(hostname, moduleNamespace, name, provider, config, cancellationToken))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient();
        var uri = BuildUpstreamUri(
            config.UpstreamRegistryBaseUrl,
            $"/v1/modules/{moduleNamespace}/{name}/{provider}/{version}");
        using var timeout = CreateTimeout(config.Modules.DownloadTimeoutSeconds, cancellationToken);
        using var response = await client.GetAsync(uri, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var module = await response.Content.ReadFromJsonAsync<TerraformModule>(JsonOptions, cancellationToken);
        if (module is null)
        {
            return null;
        }

        module.DownloadUrl = $"/v1/modules/{Uri.EscapeDataString(moduleNamespace)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(version)}/download";
        return module;
    }

    public async Task<string?> GetModuleDownloadPathAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string? localDownloadPath,
        CancellationToken cancellationToken = default)
    {
        var config = (await configService.GetConfigAsync(cancellationToken)).Effective;
        var hostname = GetUpstreamHostname(config);

        if (!string.IsNullOrWhiteSpace(localDownloadPath))
        {
            return await ApplyCachedPackageHintsAsync(
                hostname,
                moduleNamespace,
                name,
                provider,
                version,
                localDownloadPath,
                cancellationToken);
        }

        localDownloadPath = await moduleService.GetModuleDownloadPathAsync(moduleNamespace, name, provider, version);
        if (!string.IsNullOrWhiteSpace(localDownloadPath))
        {
            return await ApplyCachedPackageHintsAsync(
                hostname,
                moduleNamespace,
                name,
                provider,
                version,
                localDownloadPath,
                cancellationToken);
        }

        if (!await IsMirrorAllowedAsync(hostname, moduleNamespace, name, provider, config, cancellationToken))
        {
            return null;
        }

        var cachedLocalPath = await GetReadyPackageLocalPathAsync(hostname, moduleNamespace, name, provider, version, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cachedLocalPath))
        {
            return cachedLocalPath;
        }

        var leaseKey = $"module-package:{hostname}:{moduleNamespace}:{name}:{provider}:{version}";
        using var admission = _downloadAdmission.TryAcquire(config.Limits, leaseKey);
        if (admission is null)
        {
            RegistryLog.Warning(logger, "Module mirror admission limit reached for {LeaseKey}", leaseKey);
            return null;
        }

        var lease = await leaseService.TryAcquireAsync(leaseKey, "module-package", TimeSpan.FromMinutes(5), cancellationToken);
        if (lease is null)
        {
            return await GetReadyPackageLocalPathAsync(hostname, moduleNamespace, name, provider, version, cancellationToken);
        }

        try
        {
            cachedLocalPath = await GetReadyPackageLocalPathAsync(hostname, moduleNamespace, name, provider, version, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cachedLocalPath))
            {
                return cachedLocalPath;
            }

            return await FetchCacheAndCreateLocalDownloadPathAsync(
                hostname,
                moduleNamespace,
                name,
                provider,
                version,
                config,
                cancellationToken);
        }
        finally
        {
            await ReleaseLeaseAsync(lease);
        }
    }

    private async Task<ModuleVersions?> GetUpstreamVersionsAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        var cached = await repository.GetModuleVersionsAsync(hostname, moduleNamespace, name, provider);
        if (cached is not null &&
            string.Equals(cached.State, "not_found", StringComparison.OrdinalIgnoreCase) &&
            cached.LastSyncAt is { } negativeSync &&
            negativeSync.AddSeconds(config.Limits.NegativeCacheTtlSeconds) > DateTime.UtcNow)
        {
            return null;
        }

        if (cached is not null &&
            string.Equals(cached.State, "ready", StringComparison.OrdinalIgnoreCase) &&
            cached.LastSyncAt is { } lastSync &&
            lastSync.AddMinutes(Math.Max(1, config.Modules.MetadataTtlMinutes)) > DateTime.UtcNow)
        {
            return DeserializeVersions(cached.VersionsJson);
        }

        var client = httpClientFactory.CreateClient();
        var uri = BuildUpstreamUri(config.UpstreamRegistryBaseUrl, $"/v1/modules/{moduleNamespace}/{name}/{provider}/versions");
        using var timeout = CreateTimeout(config.Modules.DownloadTimeoutSeconds, cancellationToken);
        using var response = await client.GetAsync(uri, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await repository.UpsertModuleVersionsAsync(new MirrorModuleVersions
            {
                Hostname = hostname,
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                VersionsJson = "{}",
                State = "not_found",
                LastSyncAt = DateTime.UtcNow
            });
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var versions = DeserializeVersions(json);
        if (versions is null)
        {
            return null;
        }

        await repository.UpsertModuleVersionsAsync(new MirrorModuleVersions
        {
            Hostname = hostname,
            Namespace = moduleNamespace,
            Name = name,
            Provider = provider,
            VersionsJson = json,
            ETag = response.Headers.ETag?.Tag,
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        });

        return versions;
    }

    private async Task<string?> GetReadyPackageLocalPathAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        CancellationToken cancellationToken)
    {
        var cached = await repository.GetModulePackageAsync(hostname, moduleNamespace, name, provider, version);
        if (cached is null || !string.Equals(cached.State, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var localPath = await moduleService.GetModuleDownloadPathAsync(moduleNamespace, name, provider, version);
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return await IsLocalMirrorArtifactAsync(hostname, moduleNamespace, name, provider, version, cancellationToken)
                ? AppendPackageMetadataHints(localPath, DeserializePackageMetadata(cached.MetadataJson))
                : localPath;
        }

        await repository.MarkModulePackageFailedAsync(
            hostname,
            moduleNamespace,
            name,
            provider,
            version,
            "Cached module package artifact is missing.");
        return null;
    }

    private async Task<string> ApplyCachedPackageHintsAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string localDownloadPath,
        CancellationToken cancellationToken)
    {
        var cached = await repository.GetModulePackageAsync(hostname, moduleNamespace, name, provider, version);
        return cached is not null &&
               string.Equals(cached.State, "ready", StringComparison.OrdinalIgnoreCase) &&
               await IsLocalMirrorArtifactAsync(hostname, moduleNamespace, name, provider, version, cancellationToken)
            ? AppendPackageMetadataHints(localDownloadPath, DeserializePackageMetadata(cached.MetadataJson))
            : localDownloadPath;
    }

    private async Task<bool> IsLocalMirrorArtifactAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        CancellationToken cancellationToken)
    {
        var module = await moduleService.GetModuleAsync(moduleNamespace, name, provider, version);
        return IsMirrorArtifactFromOrigin(module, hostname);
    }

    private static bool IsMirrorArtifactFromOrigin(TerraformModule? module, string hostname)
    {
        var source = module?.Metadata?.Source;
        return source is not null &&
               string.Equals(source.Kind, "mirror", StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(source.Origin) ||
                string.Equals(source.Origin, hostname, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> FetchCacheAndCreateLocalDownloadPathAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        ModuleArchiveSource? source = null;
        try
        {
            var upstreamDownloadUri = BuildUpstreamUri(
                config.UpstreamRegistryBaseUrl,
                $"/v1/modules/{moduleNamespace}/{name}/{provider}/{version}/download");
            source = await GetUpstreamArchiveSourceAsync(
                upstreamDownloadUri,
                config.Modules.DownloadTimeoutSeconds,
                cancellationToken);

            var replaceExistingMirror = false;
            var currentModule = await moduleService.GetModuleAsync(moduleNamespace, name, provider, version);
            if (currentModule is not null)
            {
                var currentLocalPath = await moduleService.GetModuleDownloadPathAsync(
                    moduleNamespace,
                    name,
                    provider,
                    version);
                if (!IsMirrorArtifactFromOrigin(currentModule, hostname))
                {
                    return currentLocalPath;
                }

                replaceExistingMirror = true;
            }

            await repository.UpsertModulePackageAsync(new MirrorModulePackage
            {
                Hostname = hostname,
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                DownloadUrl = source.ArchiveUrl.ToString(),
                Source = source.OriginalSource,
                State = "pending",
                LastSyncAt = DateTime.UtcNow
            });

            await using var archive = (await mirrorHttpClient.FetchModuleArchiveAsync(
                source.ArchiveUrl.ToString(),
                config.Modules.MaxPackageBytes,
                config.Modules.MaxRedirects,
                config.Modules.DownloadTimeoutSeconds,
                cancellationToken)).Content;

            var sizeBytes = archive.CanSeek ? archive.Length : (long?)null;
            var metadata = CreateMetadata(hostname, source);
            var published = await publishCoordinator.PublishAsync(new ModulePublishRequest
            {
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = $"Mirrored from {hostname}",
                ModuleContent = archive,
                Replace = replaceExistingMirror,
                AuditAction = "module.mirror_cached",
                Metadata = metadata
            }, cancellationToken);

            var localPath = await moduleService.GetModuleDownloadPathAsync(moduleNamespace, name, provider, version);
            if (!published && string.IsNullOrWhiteSpace(localPath))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(localPath))
            {
                await repository.MarkModulePackageFailedAsync(
                    hostname,
                    moduleNamespace,
                    name,
                    provider,
                    version,
                    "Mirrored module package was cached but no local download path is available.");
                return null;
            }

            var packageMetadata = new ModuleMirrorPackageMetadata
            {
                PreservedSuffix = source.PreservedSuffix,
                ArchiveFormat = source.ArchiveFormat
            };
            await repository.UpsertModulePackageAsync(new MirrorModulePackage
            {
                Hostname = hostname,
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                DownloadUrl = source.ArchiveUrl.ToString(),
                Source = source.OriginalSource,
                PackageStoragePath = localPath,
                SizeBytes = sizeBytes,
                CacheSizeBytes = sizeBytes,
                MetadataJson = JsonSerializer.Serialize(packageMetadata, JsonOptions),
                State = "ready",
                LastSyncAt = DateTime.UtcNow
            });

            return AppendPreservedHints(localPath, source);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or IOException)
        {
            RegistryLog.Warning(
                logger,
                ex,
                "Module mirror fetch failed for {Hostname}/{Namespace}/{Name}/{Provider}/{Version}",
                hostname,
                moduleNamespace,
                name,
                provider,
                version);
            await MarkPackageFailedAsync(hostname, moduleNamespace, name, provider, version, source, ex);
            return null;
        }
    }

    private async Task<ModuleArchiveSource> GetUpstreamArchiveSourceAsync(
        Uri upstreamDownloadUri,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(MirrorDiscoveryHttpClientName);
        using var timeout = CreateTimeout(timeoutSeconds, cancellationToken);
        using var response = await client.GetAsync(upstreamDownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Upstream module package was not found.");
        }

        if (IsRedirect(response.StatusCode))
        {
            throw new InvalidOperationException("Upstream module download discovery redirects are not allowed.");
        }

        response.EnsureSuccessStatusCode();
        if (!response.Headers.TryGetValues("X-Terraform-Get", out var values))
        {
            throw new InvalidOperationException("Upstream module download response did not include X-Terraform-Get.");
        }

        var source = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("Upstream module download response included an empty X-Terraform-Get.");
        }

        return ResolveArchiveSource(source, response.RequestMessage?.RequestUri ?? upstreamDownloadUri);
    }

    private static ModuleArchiveSource ResolveArchiveSource(string xTerraformGet, Uri responseUri)
    {
        var trimmed = xTerraformGet.Trim();
        if (LooksLikeRecursiveModuleRegistryAddress(trimmed))
        {
            throw new InvalidOperationException("Recursive module registry X-Terraform-Get addresses are not allowed.");
        }

        var packagePart = trimmed;
        string? goGetterSuffix = null;
        var goGetterDelimiter = FindGoGetterSubdirectoryDelimiter(trimmed);
        if (goGetterDelimiter >= 0)
        {
            packagePart = trimmed[..goGetterDelimiter];
            goGetterSuffix = trimmed[goGetterDelimiter..];
        }

        if (LooksLikeRecursiveModuleRegistryAddress(packagePart))
        {
            throw new InvalidOperationException("Recursive module registry X-Terraform-Get addresses are not allowed.");
        }

        var isRelativeReference = packagePart.Length > 0 && packagePart[0] == '/' ||
                                  packagePart.StartsWith("./", StringComparison.Ordinal) ||
                                  packagePart.StartsWith("../", StringComparison.Ordinal);
        Uri archiveUri;
        if (isRelativeReference || !Uri.TryCreate(packagePart, UriKind.Absolute, out var parsedArchiveUri))
        {
            archiveUri = new Uri(responseUri, packagePart);
        }
        else
        {
            archiveUri = parsedArchiveUri;
        }

        var archiveFormat = GetArchiveFormat(goGetterSuffix);
        if (string.IsNullOrWhiteSpace(archiveFormat))
        {
            archiveUri = RemoveArchiveQueryParameter(archiveUri, out archiveFormat);
        }

        return new ModuleArchiveSource(
            trimmed,
            archiveUri,
            goGetterSuffix,
            archiveFormat);
    }

    private async Task MarkPackageFailedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        ModuleArchiveSource? source,
        Exception ex)
    {
        if (source is null)
        {
            await repository.UpsertModulePackageAsync(new MirrorModulePackage
            {
                Hostname = hostname,
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                DownloadUrl = "unknown",
                State = "pending"
            });
        }

        await repository.MarkModulePackageFailedAsync(
            hostname,
            moduleNamespace,
            name,
            provider,
            version,
            ex.Message,
            ex is HttpRequestException httpEx ? (int?)httpEx.StatusCode : null);
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
                "Failed to release module mirror lease {LeaseKey} held by {OwnerInstanceId}",
                lease.LeaseKey,
                lease.OwnerInstanceId);
        }
    }

    private async Task<bool> IsMirrorAllowedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        MirrorOptions config,
        CancellationToken cancellationToken)
    {
        return config.Enabled &&
               config.Modules.Enabled &&
               await policyService.IsModuleAllowedAsync(hostname, moduleNamespace, name, provider, cancellationToken);
    }

    private static ModuleArtifactMetadata CreateMetadata(string hostname, ModuleArchiveSource source) =>
        new()
        {
            RootSubdirectory = GetRootSubdirectory(source.PreservedSuffix),
            Source = new ModuleSourceInfo
            {
                Kind = "mirror",
                Origin = hostname,
                SourceUrl = source.OriginalSource,
                ResolvedPackageUrl = source.ArchiveUrl.ToString(),
                ArchiveFormat = source.ArchiveFormat
            }
        };

    private static ModuleVersions MergeVersions(ModuleVersions localVersions, ModuleVersions upstreamVersions)
    {
        var merged = new SortedDictionary<string, VersionInfo>(StringComparer.Ordinal);
        foreach (var version in GetVersions(upstreamVersions))
        {
            merged[version.Version] = version;
        }

        foreach (var version in GetVersions(localVersions))
        {
            merged[version.Version] = version;
        }

        return new ModuleVersions
        {
            Modules =
            [
                new ModuleVersionInfo
                {
                    Versions = merged.Values.ToList()
                }
            ]
        };
    }

    private static List<VersionInfo> GetVersions(ModuleVersions versions) =>
        versions.Modules.FirstOrDefault()?.Versions ?? [];

    private static ModuleVersions? DeserializeVersions(string json) =>
        JsonSerializer.Deserialize<ModuleVersions>(json, JsonOptions);

    private static ModuleMirrorPackageMetadata? DeserializePackageMetadata(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleMirrorPackageMetadata>(json, JsonOptions);

    private static Uri BuildUpstreamUri(string upstreamBaseUrl, string path)
    {
        var baseUri = new Uri(upstreamBaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private static string GetUpstreamHostname(MirrorOptions config) =>
        new Uri(config.UpstreamRegistryBaseUrl.TrimEnd('/') + "/").DnsSafeHost;

    private static CancellationTokenSource CreateTimeout(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return timeout;
    }

    private static string AppendPreservedHints(string localPath, ModuleArchiveSource source)
    {
        if (!string.IsNullOrEmpty(source.PreservedSuffix))
        {
            return localPath + source.PreservedSuffix;
        }

        if (string.IsNullOrWhiteSpace(source.ArchiveFormat))
        {
            return localPath;
        }

        var separator = localPath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{localPath}{separator}archive={Uri.EscapeDataString(source.ArchiveFormat)}";
    }

    private static string AppendPackageMetadataHints(string localPath, ModuleMirrorPackageMetadata? metadata)
    {
        if (metadata is null)
        {
            return localPath;
        }

        if (!string.IsNullOrEmpty(metadata.PreservedSuffix))
        {
            return localPath + metadata.PreservedSuffix;
        }

        if (string.IsNullOrWhiteSpace(metadata.ArchiveFormat))
        {
            return localPath;
        }

        var separator = localPath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{localPath}{separator}archive={Uri.EscapeDataString(metadata.ArchiveFormat)}";
    }

    private static int FindGoGetterSubdirectoryDelimiter(string source)
    {
        var searchStart = 0;
        var schemeIndex = source.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            searchStart = schemeIndex + 3;
        }

        return source.IndexOf("//", searchStart, StringComparison.Ordinal);
    }

    private static bool LooksLikeRecursiveModuleRegistryAddress(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return LooksLikeRegistryModuleHost(uri) && LooksLikeModuleAddressPath(uri.AbsolutePath);
        }

        if (source[0] == '/' ||
            source.StartsWith("./", StringComparison.Ordinal) ||
            source.StartsWith("../", StringComparison.Ordinal))
        {
            return false;
        }

        return LooksLikeModuleAddressPath(source);
    }

    private static bool LooksLikeRegistryModuleHost(Uri uri)
    {
        return string.Equals(uri.Host, "registry.terraform.io", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("registry.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeModuleAddressPath(string source)
    {
        var path = source.Split('?', 2)[0].Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 3 or 4;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static Uri RemoveArchiveQueryParameter(Uri uri, out string? archiveFormat)
    {
        archiveFormat = null;
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("archive", out var archiveValues))
        {
            return uri;
        }

        archiveFormat = archiveValues.FirstOrDefault();
        var pairs = query
            .Where(pair => !string.Equals(pair.Key, "archive", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value
                .Where(value => value is not null)
                .Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
        var builder = new UriBuilder(uri)
        {
            Query = QueryString.Create(pairs).ToString().TrimStart('?')
        };
        return builder.Uri;
    }

    private static string? GetArchiveFormat(string? goGetterSuffix)
    {
        if (string.IsNullOrWhiteSpace(goGetterSuffix))
        {
            return null;
        }

        var queryIndex = goGetterSuffix.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(goGetterSuffix[queryIndex..]);
        return query.TryGetValue("archive", out var archive) ? archive.FirstOrDefault() : null;
    }

    private static string? GetRootSubdirectory(string? goGetterSuffix)
    {
        if (string.IsNullOrWhiteSpace(goGetterSuffix) || !goGetterSuffix.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var subdirectory = goGetterSuffix[2..].Split('?', 2)[0];
        return string.IsNullOrWhiteSpace(subdirectory) ? null : subdirectory;
    }

    private sealed record ModuleArchiveSource(
        string OriginalSource,
        Uri ArchiveUrl,
        string? PreservedSuffix,
        string? ArchiveFormat);

    private sealed class ModuleMirrorPackageMetadata
    {
        public string? PreservedSuffix { get; set; }
        public string? ArchiveFormat { get; set; }
    }
}
