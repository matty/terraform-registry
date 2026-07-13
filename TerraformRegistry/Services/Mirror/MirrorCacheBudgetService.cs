using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

/// <summary>Maintains the configured mirror cache budget with deterministic oldest-first eviction.</summary>
public sealed class MirrorCacheBudgetService(
    IProviderMirrorRepository providerRepository,
    IModuleMirrorRepository moduleRepository,
    IProviderArtifactStorage providerStorage,
    IModuleService moduleService)
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
            if (totalBytes + additionalBytes <= maximumBytes)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> EvictAsync(EvictionCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Provider is { } provider)
        {
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
            return true;
        }

        var module = candidate.Module!;
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
        return true;
    }

    private async Task<List<MirrorProviderPackage>> ListProviderPackagesAsync(CancellationToken cancellationToken)
    {
        var packages = new List<MirrorProviderPackage>();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await providerRepository.ListProviderPackagesAsync(null, "ready", PageSize, offset);
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
            packages.AddRange(page);
            if (page.Count < PageSize) return packages;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static long CacheBytes(MirrorProviderPackage package) => package.CacheSizeBytes ?? package.SizeBytes ?? 0;
    private static long CacheBytes(MirrorModulePackage package) => package.CacheSizeBytes ?? package.SizeBytes ?? 0;

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
