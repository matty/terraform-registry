using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Stores module catalog metadata and lifecycle state.
/// </summary>
public interface IModuleRepository
{
    /// <summary>
    ///     Lists all modules based on search criteria.
    /// </summary>
    Task<ModuleList> ListModulesAsync(ModuleSearchRequest request);

    /// <summary>
    ///     Gets detailed information about a specific module.
    /// </summary>
    Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Gets all versions of a specific module.
    /// </summary>
    Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider);

    /// <summary>
    ///     Gets the storage path information for a specific module version.
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Adds a new module to the database.
    /// </summary>
    Task<bool> AddModuleAsync(ModuleStorage module);

    /// <summary>
    ///     Removes a module from the database permanently.
    /// </summary>
    Task<bool> RemoveModuleAsync(ModuleStorage module);

    /// <summary>
    ///     Removes a module row only when its stored metadata matches the provided module.
    /// </summary>
    Task<bool> RemoveModuleExactAsync(ModuleStorage module);

    /// <summary>
    ///     Removes a module row only when it is currently soft-deleted.
    /// </summary>
    Task<bool> RemoveDeletedModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Adds a module row directly in the soft-deleted state.
    /// </summary>
    Task<bool> AddDeletedModuleAsync(ModuleStorage module);

    /// <summary>
    ///     Replaces a module row only when its stored metadata matches the expected current module.
    /// </summary>
    Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule);

    /// <summary>
    ///     Soft deletes a module by setting its deleted timestamp.
    /// </summary>
    Task<bool> SoftDeleteModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Restores a soft-deleted module by clearing its deleted timestamp.
    /// </summary>
    Task<bool> RestoreModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Lists all soft-deleted modules.
    /// </summary>
    Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request);

    /// <summary>
    ///     Gets a module including soft-deleted ones.
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(
        string @namespace,
        string name,
        string provider,
        string version);

    /// <summary>
    ///     Updates the description for all active versions of a module.
    /// </summary>
    Task<bool> UpdateModuleDescriptionAsync(string @namespace, string name, string provider, string description);
}
