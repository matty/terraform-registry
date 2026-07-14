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
    Task<ModuleList> ListModulesAsync(ModuleSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets detailed information about a specific module.
    /// </summary>
    Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all versions of a specific module.
    /// </summary>
    Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the storage path information for a specific module version.
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds a new module to the database.
    /// </summary>
    Task<bool> AddModuleAsync(ModuleStorage moduleStorage, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a module from the database permanently.
    /// </summary>
    Task<bool> RemoveModuleAsync(ModuleStorage moduleStorage, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a module row only when its stored metadata matches the provided module.
    /// </summary>
    Task<bool> RemoveModuleExactAsync(ModuleStorage moduleStorage, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Removes a module row only when it is currently soft-deleted.
    /// </summary>
    Task<bool> RemoveDeletedModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Adds a module row directly in the soft-deleted state.
    /// </summary>
    Task<bool> AddDeletedModuleAsync(ModuleStorage moduleStorage, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces a module row only when its stored metadata matches the expected current module.
    /// </summary>
    Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule);

    /// <summary>
    ///     Soft deletes a module by setting its deleted timestamp.
    /// </summary>
    Task<bool> SoftDeleteModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Restores a soft-deleted module by clearing its deleted timestamp.
    /// </summary>
    Task<bool> RestoreModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all soft-deleted modules.
    /// </summary>
    Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a module including soft-deleted ones.
    /// </summary>
    Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the description for all active versions of a module.
    /// </summary>
    Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider, string description,
        CancellationToken cancellationToken = default);
}
