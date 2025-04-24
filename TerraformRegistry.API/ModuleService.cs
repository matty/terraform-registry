namespace TerraformRegistry.API;

using Interfaces;
using Models;
using Utilities;

/// <summary>
/// Abstract base class for module services that implements SemVer validation
/// </summary>
public abstract class ModuleService : IModuleService
{
    /// <summary>
    /// Lists all modules
    /// </summary>
    public abstract Task<ModuleList> ListModulesAsync(ModuleSearchRequest request);

    /// <summary>
    /// Gets detailed information about a specific module
    /// </summary>
    public abstract Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    /// Gets all versions of a specific module
    /// </summary>
    public abstract Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider);

    /// <summary>
    /// Gets the download path for a specific module version
    /// </summary>
    public abstract Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider, string version);

    /// <summary>
    /// Uploads a new module with SemVer validation
    /// </summary>
    public async Task<bool> UploadModuleAsync(string @namespace, string name, string provider, string version, Stream moduleContent, string description)
    {
        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            throw new ArgumentException($"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]", nameof(version));
        }

        // Delegate to the implementation-specific upload method
        return await UploadModuleAsyncImpl(@namespace, name, provider, version, moduleContent, description);
    }

    /// <summary>
    /// Implementation-specific method to upload a module after validation
    /// </summary>
    protected abstract Task<bool> UploadModuleAsyncImpl(string @namespace, string name, string provider, string version, Stream moduleContent, string description);
}