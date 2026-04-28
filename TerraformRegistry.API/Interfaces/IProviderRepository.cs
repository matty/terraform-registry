using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderRepository
{
    Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit);
    Task<TerraformProvider?> GetProviderAsync(string @namespace, string type);
    Task<TerraformProvider> CreateProviderAsync(TerraformProvider provider);
    Task<bool> UpdateProviderAsync(string @namespace, string type, string? displayName, string? description, string? sourceRepositoryUrl);
    Task<bool> DeleteProviderAsync(string @namespace, string type);

    Task<IReadOnlyList<ProviderVersionEntry>> GetProviderVersionsAsync(string @namespace, string type);
    Task<IReadOnlyList<ProviderManagementVersionEntry>> GetProviderManagementVersionsAsync(string @namespace, string type);
    Task<ProviderVersion?> GetProviderVersionAsync(string @namespace, string type, string version);
    Task<ProviderVersion> CreateProviderVersionAsync(Guid providerId, string version, string[] protocols, string keyId);
    Task<bool> SetVersionShasumsPathAsync(Guid versionId, string storagePath);
    Task<bool> SetVersionShasumsSignaturePathAsync(Guid versionId, string storagePath);
    Task<bool> DeleteProviderVersionAsync(string @namespace, string type, string version);

    Task<IReadOnlyList<ProviderManagementPlatformEntry>> GetProviderManagementPlatformsAsync(string @namespace, string type, string version);
    Task<ProviderPlatform?> GetProviderPlatformAsync(string @namespace, string type, string version, string os, string arch);
    Task<ProviderPlatform> CreateProviderPlatformAsync(Guid versionId, string os, string arch, string filename, string shasum);
    Task<bool> SetPlatformPackagePathAsync(Guid platformId, string storagePath, long sizeBytes);
    Task<bool> DeleteProviderPlatformAsync(string @namespace, string type, string version, string os, string arch);

    Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string @namespace);
    Task<ProviderGpgKey?> GetGpgKeyAsync(string @namespace, string keyId);
    Task<ProviderGpgKey> AddGpgKeyAsync(ProviderGpgKey key);
    Task<bool> RevokeGpgKeyAsync(string @namespace, string keyId);

    Task RecordProviderDownloadAsync(Guid? providerId, string @namespace, string type, string version, string os,
        string arch, string? clientIp, string? userAgent);
}
