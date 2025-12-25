using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Interface for database services that store module metadata
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    ///     Lists all modules based on search criteria
    /// </summary>
    Task<ModuleList> ListModulesAsync(ModuleSearchRequest request);

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider);

    /// <summary>
    ///     Gets the storage path information for a specific module version
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Adds a new module to the database
    /// </summary>
    Task<bool> AddModuleAsync(ModuleStorage module);

    /// <summary>
    ///     Removes a module from the database
    /// </summary>
    Task<bool> RemoveModuleAsync(ModuleStorage module);

    // User & API Key Methods
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdAsync(string id);
    Task AddUserAsync(User user);
    Task UpdateUserAsync(User user);
    Task DeleteUserAsync(string userId);

    Task AddApiKeyAsync(ApiKey apiKey);
    Task<ApiKey?> GetApiKeyAsync(Guid id);
    Task<IEnumerable<ApiKey>> GetApiKeysByUserAsync(string userId);
    Task<IEnumerable<ApiKey>> GetSharedApiKeysAsync();
    Task<IEnumerable<ApiKey>> GetApiKeysByPrefixAsync(string prefix);
    Task UpdateApiKeyAsync(ApiKey apiKey);
    Task DeleteApiKeyAsync(ApiKey apiKey);

    // Provider Methods
    Task<ProviderVersions?> GetProviderVersionsAsync(string @namespace, string type);
    Task<ProviderPackage?> GetProviderPackageAsync(string @namespace, string type, string version, string os, string arch);
    Task AddProviderPackageAsync(string @namespace, string type, string version, string os, string arch, string filename, string downloadUrl, string shasum, string protocolsJson, string signingKeyId);
    Task<GpgKey?> GetGpgKeyAsync(string @namespace, string keyId);
    Task<IEnumerable<GpgKey>> GetGpgKeysAsync(string @namespace);
    Task AddGpgKeyAsync(GpgKey key);
}