using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderRepository
{
    Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit);
    Task<int> CountProvidersAsync(string? q);
    Task<TerraformProvider?> GetProviderAsync(string providerNamespace, string type);
    Task<TerraformProvider> CreateProviderAsync(TerraformProvider provider);
    Task<bool> UpdateProviderAsync(string providerNamespace, string type, string? displayName, string? description, string? sourceRepositoryUrl);
    Task<bool> DeleteProviderAsync(string providerNamespace, string type);

    Task<IReadOnlyList<ProviderVersionEntry>> GetProviderVersionsAsync(string providerNamespace, string type);
    Task<IReadOnlyList<ProviderManagementVersionEntry>> GetProviderManagementVersionsAsync(string providerNamespace, string type);
    Task<ProviderVersion?> GetProviderVersionAsync(string providerNamespace, string type, string version);
    Task<ProviderPackageDetails?> GetProviderPackageDetailsAsync(string providerNamespace, string type, string version, string os, string arch) =>
        Task.FromResult<ProviderPackageDetails?>(null);
    Task<ProviderVersion> CreateProviderVersionAsync(Guid providerId, string version, string[] protocols, string keyId);
    Task<bool> SetVersionShasumsPathAsync(Guid versionId, string storagePath);
    Task<bool> SetVersionShasumsSignaturePathAsync(Guid versionId, string storagePath);
    Task<bool> DeleteProviderVersionAsync(string providerNamespace, string type, string version);
    Task<IReadOnlyList<string>> GetProviderArtifactStoragePathsAsync(string providerNamespace, string type, string? version, string? os, string? arch);

    Task<IReadOnlyList<ProviderManagementPlatformEntry>> GetProviderManagementPlatformsAsync(string providerNamespace, string type, string version);
    Task<ProviderPlatform?> GetProviderPlatformAsync(string providerNamespace, string type, string version, string os, string arch);
    Task<ProviderPlatform> CreateProviderPlatformAsync(Guid versionId, string os, string arch, string filename, string shasum);
    Task<bool> SetPlatformPackagePathAsync(Guid platformId, string storagePath, long sizeBytes);
    Task<bool> DeleteProviderPlatformAsync(string providerNamespace, string type, string version, string os, string arch);

    Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string providerNamespace);
    Task<ProviderGpgKey?> GetGpgKeyAsync(string providerNamespace, string keyId);
    Task<ProviderGpgKey> AddGpgKeyAsync(ProviderGpgKey key);
    Task<bool> ProviderGpgKeyIsReferencedByActiveVersionsAsync(string providerNamespace, string keyId);
    Task<bool> RevokeGpgKeyAsync(string providerNamespace, string keyId);

    Task RecordProviderDownloadAsync(Guid? providerId, string providerNamespace, string type, string version, string os,
        string arch, string? clientIp, string? userAgent);
}
