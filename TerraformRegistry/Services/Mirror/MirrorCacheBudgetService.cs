using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

/// <summary>Maintains the configured mirror cache budget with deterministic oldest-first eviction.</summary>
public sealed class MirrorCacheBudgetService(
    IProviderMirrorRepository providerRepository,
    IModuleMirrorRepository moduleRepository,
    IProviderArtifactStorage providerStorage,
    IModuleService moduleService,
    MirrorCacheUsage cacheUsage,
    IMirrorLeaseRepository? leaseRepository = null,
    OperationalMetrics? metrics = null)
{
    private const int PageSize = 1000;

    public async Task<bool> EnsureCapacityAsync(long additionalBytes, long maximumBytes, CancellationToken cancellationToken)
    {
        if (additionalBytes < 0 || maximumBytes <= 0 || additionalBytes > maximumBytes)
        {
            return false;
        }

        var providers = await ListProviderPackagesAsync(cancellationToken);
        var modules = await ListModulePackagesAsync(cancellationToken);
        var totalBytes = providers.Sum(CacheBytes) + modules.Sum(CacheBytes);
        metrics?.RecordMirrorCacheBytes(totalBytes);
        if (totalBytes + additionalBytes <= maximumBytes)
        {
            return true;
        }

        var candidates = providers.Select(package => new EvictionCandidate(package, null))
            .Concat(modules.Select(package => new EvictionCandidate(null, package)))
            .OrderBy(candidate => candidate.UpdatedAt)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (!await EvictAsync(candidate, cancellationToken))
            {
                continue;
            }

            totalBytes -= candidate.Bytes;
            metrics?.RecordMirrorCacheBytes(totalBytes);
            if (totalBytes + additionalBytes <= maximumBytes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records the cache size after a successful cache lifecycle change.</summary>
    public async Task RecordCacheBytesAsync(CancellationToken cancellationToken)
    {
        var providers = await ListProviderPackagesAsync(cancellationToken);
        var modules = await ListModulePackagesAsync(cancellationToken);
        metrics?.RecordMirrorCacheBytes(providers.Sum(CacheBytes) + modules.Sum(CacheBytes));
    }

    public async Task<MirrorCachePurgeResult> PurgeProviderAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        CancellationToken cancellationToken)
    {
        var package = await providerRepository.GetProviderPackageAsync(
            hostname, providerNamespace, type, version, os, arch);
        if (package is null || string.IsNullOrWhiteSpace(package.PackageStoragePath))
        {
            return MirrorCachePurgeResult.NotFound;
        }

        if (await IsInUseAsync(package, cancellationToken))
        {
            return MirrorCachePurgeResult.InUse;
        }

        if (!await EvictAsync(new EvictionCandidate(package, null), cancellationToken))
        {
            return MirrorCachePurgeResult.Failed;
        }

        await RecordCacheBytesAsync(cancellationToken);
        return MirrorCachePurgeResult.Purged;
    }

    public async Task<MirrorCachePurgeResult> PurgeModuleAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        CancellationToken cancellationToken)
    {
        var package = await moduleRepository.GetModulePackageAsync(
            hostname, moduleNamespace, name, provider, version);
        if (package is null || string.IsNullOrWhiteSpace(package.PackageStoragePath))
        {
            return MirrorCachePurgeResult.NotFound;
        }

        if (await IsInUseAsync(package, cancellationToken))
        {
            return MirrorCachePurgeResult.InUse;
        }

        if (!await EvictAsync(new EvictionCandidate(null, package), cancellationToken))
        {
            return MirrorCachePurgeResult.Failed;
        }

        await RecordCacheBytesAsync(cancellationToken);
        return MirrorCachePurgeResult.Purged;
    }

    private async Task<bool> EvictAsync(EvictionCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Provider is { } provider)
        {
            if (await IsInUseAsync(provider, cancellationToken))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(provider.PackageStoragePath) ||
                !await providerStorage.DeleteAsync(provider.PackageStoragePath, cancellationToken))
            {
                return false;
            }

            await providerRepository.UpsertProviderPackageAsync(provider with
            {
                PackageStoragePath = null,
                SizeBytes = null,
                CacheSizeBytes = 0,
                State = "evicted",
                LastError = "Evicted to enforce the mirror cache budget.",
                LastSyncAt = DateTime.UtcNow
            });
            metrics?.RecordMirrorEviction("provider");
            return true;
        }

        var module = candidate.Module!;
        if (await IsInUseAsync(module, cancellationToken))
        {
            return false;
        }
        if (!await moduleService.PurgeModuleVersionAsync(module.Namespace, module.Name, module.Provider, module.Version))
        {
            return false;
        }

        await moduleRepository.UpsertModulePackageAsync(module with
        {
            PackageStoragePath = null,
            SizeBytes = null,
            CacheSizeBytes = 0,
            State = "evicted",
            LastError = "Evicted to enforce the mirror cache budget.",
            LastSyncAt = DateTime.UtcNow
        });
        metrics?.RecordMirrorEviction("module");
        return true;
    }

    private async Task<List<MirrorProviderPackage>> ListProviderPackagesAsync(CancellationToken cancellationToken)
    {
        var packages = new List<MirrorProviderPackage>();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await providerRepository.ListProviderPackagesAsync(null, "ready", PageSize, offset);
            ArgumentNullException.ThrowIfNull(page);
            if (page.Count == 0) return packages;

            packages.AddRange(page);
            if (page.Count < PageSize) return packages;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task<List<MirrorModulePackage>> ListModulePackagesAsync(CancellationToken cancellationToken)
    {
        var packages = new List<MirrorModulePackage>();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await moduleRepository.ListModulePackagesAsync(null, "ready", PageSize, offset);
            ArgumentNullException.ThrowIfNull(page);
            if (page.Count == 0) return packages;

            packages.AddRange(page);
            if (page.Count < PageSize) return packages;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static long CacheBytes(MirrorProviderPackage package) => package.CacheSizeBytes ?? package.SizeBytes ?? 0;
    private static long CacheBytes(MirrorModulePackage package) => package.CacheSizeBytes ?? package.SizeBytes ?? 0;
    private async Task<bool> IsInUseAsync(MirrorProviderPackage package, CancellationToken cancellationToken) =>
        cacheUsage.IsInUse(ProviderKey(package)) || await HasActiveLeaseAsync(ProviderLeaseKey(package), cancellationToken);
    private async Task<bool> IsInUseAsync(MirrorModulePackage package, CancellationToken cancellationToken) =>
        cacheUsage.IsInUse(ModuleKey(package)) || await HasActiveLeaseAsync(ModuleLeaseKey(package), cancellationToken);
    private async Task<bool> HasActiveLeaseAsync(string leaseKey, CancellationToken cancellationToken)
    {
        var lease = leaseRepository is null ? null : await leaseRepository.GetLeaseAsync(leaseKey, cancellationToken);
        return lease?.ExpiresAt > DateTime.UtcNow;
    }
    internal static string ProviderKey(MirrorProviderPackage package) =>
        $"provider:{package.Hostname}:{package.Namespace}:{package.Type}:{package.Version}:{package.Os}:{package.Arch}";
    internal static string ModuleKey(MirrorModulePackage package) =>
        $"module:{package.Hostname}:{package.Namespace}:{package.Name}:{package.Provider}:{package.Version}";
    private static string ProviderLeaseKey(MirrorProviderPackage package) =>
        $"provider-package:{package.Hostname}:{package.Namespace}:{package.Type}:{package.Version}:{package.Os}:{package.Arch}";
    private static string ModuleLeaseKey(MirrorModulePackage package) =>
        $"module-package:{package.Hostname}:{package.Namespace}:{package.Name}:{package.Provider}:{package.Version}";

    private sealed class EvictionCandidate(MirrorProviderPackage? provider, MirrorModulePackage? module)
    {
        public MirrorProviderPackage? Provider { get; } = provider;
        public MirrorModulePackage? Module { get; } = module;
        public long Bytes => Provider is not null ? CacheBytes(Provider) : CacheBytes(Module!);
        public DateTime UpdatedAt => Provider?.UpdatedAt ?? Module!.UpdatedAt;
        public string Key => Provider is not null
            ? $"provider:{Provider.Hostname}:{Provider.Namespace}:{Provider.Type}:{Provider.Version}:{Provider.Os}:{Provider.Arch}"
            : $"module:{Module!.Hostname}:{Module.Namespace}:{Module.Name}:{Module.Provider}:{Module.Version}";
    }
}

public enum MirrorCachePurgeResult
{
    Purged,
    NotFound,
    InUse,
    Failed
}
