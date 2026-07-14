using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Interface for module operations in the Terraform Registry
/// </summary>
public interface IModuleService
{
    /// <summary>
    ///     Performs required storage initialization after database migration has completed.
    /// </summary>
    Task InitializeStorageAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Reconciles persisted storage state after startup readiness has been established.
    /// </summary>
    Task ReconcileStorageAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Lists all modules
    /// </summary>
    Task<ModuleList> ListModulesAsync(ModuleSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the download path for a specific module version
    /// </summary>
    Task<string?> GetModuleDownloadPathAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens the stored module package for internal processing
    /// </summary>
    Task<Stream?> OpenModulePackageStreamAsync(string moduleNamespace, string name, string provider, string version);

    /// <summary>
    ///     Uploads a new module
    /// </summary>
    Task<bool> UploadModuleAsync(string moduleNamespace, string name, string provider, string version, Stream moduleContent,
        string description, bool replace = false, ModuleArtifactMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Soft deletes a module version
    /// </summary>
    Task<bool> DeleteModuleVersionAsync(string moduleNamespace, string name, string provider, string version);

    /// <summary>
    ///     Restores a soft-deleted module version
    /// </summary>
    Task<bool> RestoreModuleVersionAsync(string moduleNamespace, string name, string provider, string version);

    /// <summary>
    ///     Permanently deletes a module version (purge)
    /// </summary>
    Task<bool> PurgeModuleVersionAsync(string moduleNamespace, string name, string provider, string version);

    /// <summary>
    ///     Lists all soft-deleted modules
    /// </summary>
    Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Updates the description for all active versions of a module
    /// </summary>
    Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider, string description);

    /// <summary>
    ///     Checks that the storage backend is healthy and writable
    /// </summary>
    Task<(bool Healthy, string? Reason)> CheckStorageAsync();
}
