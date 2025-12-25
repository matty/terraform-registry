using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
/// Interface for Provider Service
/// </summary>
public interface IProviderService
{
    Task<ProviderVersions?> GetProviderVersionsAsync(string @namespace, string type);
    Task<ProviderPackage?> GetProviderPackageAsync(string @namespace, string type, string version, string os, string arch);
    Task<ProviderPackage> UploadProviderAsync(string @namespace, string type, string version, string os, string arch, string filename, Stream stream, string shasum, string signingKeyId, List<string>? protocols = null);
    Task UploadShasumsAsync(string @namespace, string type, string version, Stream stream);
    Task UploadShasumsSigAsync(string @namespace, string type, string version, Stream stream);

    // GPG Key Management
    Task<IEnumerable<GpgKey>> GetGpgKeysAsync(string @namespace);
    Task<GpgKey?> GetGpgKeyAsync(string @namespace, string keyId);
    Task AddGpgKeyAsync(GpgKey key);
}
