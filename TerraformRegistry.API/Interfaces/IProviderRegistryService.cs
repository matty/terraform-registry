using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderRegistryService
{
    Task<ProviderVersionsResponse?> GetVersionsAsync(string providerNamespace, string type);
    Task<ProviderManagementVersionsResponse?> GetManagementVersionsAsync(string providerNamespace, string type);
    Task<ProviderManagementPlatformsResponse?> GetManagementPlatformsAsync(string providerNamespace, string type, string version);
    Task<ProviderPackageResponse?> GetPackageAsync(string providerNamespace, string type, string version, string os, string arch,
        string? clientIp, string? userAgent, CancellationToken cancellationToken);

    Task<TerraformProvider> CreateProviderAsync(CreateProviderRequest request, string? actorUserId);
    Task<IReadOnlyList<TerraformProvider>> ListProvidersAsync(string? q, int offset, int limit);
    Task<int> CountProvidersAsync(string? q);
    Task<TerraformProvider?> GetProviderAsync(string providerNamespace, string type);
    Task<bool> UpdateProviderAsync(string providerNamespace, string type, UpdateProviderRequest request);
    Task<bool> DeleteProviderAsync(string providerNamespace, string type);

    Task<ProviderGpgKey> AddGpgKeyAsync(string providerNamespace, CreateProviderGpgKeyRequest request);
    Task<IReadOnlyList<ProviderGpgKey>> ListGpgKeysAsync(string providerNamespace);
    Task<bool> RevokeGpgKeyAsync(string providerNamespace, string keyId);

    Task<ProviderVersion> CreateVersionAsync(string providerNamespace, string type, CreateProviderVersionRequest request);
    Task<bool> UploadShasumsAsync(string providerNamespace, string type, string version, Stream content, CancellationToken cancellationToken);
    Task<bool> UploadShasumsAsync(
        string providerNamespace,
        string type,
        string version,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken) => UploadShasumsAsync(providerNamespace, type, version, content, cancellationToken);
    Task<bool> UploadShasumsSignatureAsync(string providerNamespace, string type, string version, Stream content, CancellationToken cancellationToken);
    Task<bool> UploadShasumsSignatureAsync(
        string providerNamespace,
        string type,
        string version,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken) => UploadShasumsSignatureAsync(providerNamespace, type, version, content, cancellationToken);
    Task<bool> DeleteVersionAsync(string providerNamespace, string type, string version);

    Task<ProviderPlatform> CreatePlatformAsync(string providerNamespace, string type, string version, CreateProviderPlatformRequest request);
    Task<bool> UploadPlatformPackageAsync(string providerNamespace, string type, string version, string os, string arch, Stream package,
        CancellationToken cancellationToken);
    Task<bool> DeletePlatformAsync(string providerNamespace, string type, string version, string os, string arch);
}
