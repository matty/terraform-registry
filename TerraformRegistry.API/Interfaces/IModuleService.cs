using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Interface for module operations in the Terraform Registry
/// </summary>
public interface IModuleService
{
    /// <summary>
    ///     Lists all modules
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
    ///     Gets the download path for a specific module version
    /// </summary>
    Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    ///     Uploads a new module
    /// </summary>
    Task<bool> UploadModuleAsync(string @namespace, string name, string provider, string version, Stream moduleContent,
        string description, bool replace = false);
}