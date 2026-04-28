using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderRegistryService
{
    Task<ProviderVersionsResponse?> GetVersionsAsync(string @namespace, string type);
    Task<ProviderPackageResponse?> GetPackageAsync(string @namespace, string type, string version, string os, string arch,
        string? clientIp, string? userAgent, CancellationToken cancellationToken);

    Task<TerraformProvider> CreateProviderAsync(CreateProviderRequest request, string? actorUserId);
    Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit);
    Task<TerraformProvider?> GetProviderAsync(string @namespace, string type);
    Task<bool> UpdateProviderAsync(string @namespace, string type, UpdateProviderRequest request);
    Task<bool> DeleteProviderAsync(string @namespace, string type);

    Task<ProviderGpgKey> AddGpgKeyAsync(string @namespace, CreateProviderGpgKeyRequest request);
    Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string @namespace);
    Task<bool> RevokeGpgKeyAsync(string @namespace, string keyId);

    Task<ProviderVersion> CreateVersionAsync(string @namespace, string type, CreateProviderVersionRequest request);
    Task<bool> UploadShasumsAsync(string @namespace, string type, string version, Stream content, CancellationToken cancellationToken);
    Task<bool> UploadShasumsSignatureAsync(string @namespace, string type, string version, Stream content, CancellationToken cancellationToken);
    Task<bool> DeleteVersionAsync(string @namespace, string type, string version);

    Task<ProviderPlatform> CreatePlatformAsync(string @namespace, string type, string version, CreateProviderPlatformRequest request);
    Task<bool> UploadPlatformPackageAsync(string @namespace, string type, string version, string os, string arch, Stream package,
        CancellationToken cancellationToken);
    Task<bool> DeletePlatformAsync(string @namespace, string type, string version, string os, string arch);
}
