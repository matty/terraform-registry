namespace TerraformRegistry.API.Interfaces;

using Models;

/// <summary>
/// Interface for database services that store module metadata
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// Lists all modules based on search criteria
    /// </summary>
    Task<ModuleList> ListModulesAsync(ModuleSearchRequest request);

    /// <summary>
    /// Gets detailed information about a specific module
    /// </summary>
    Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    /// Gets all versions of a specific module
    /// </summary>
    Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider);

    /// <summary>
    /// Gets the storage path information for a specific module version
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    /// Adds a new module to the database
    /// </summary>
    Task<bool> AddModuleAsync(ModuleStorage module);
}