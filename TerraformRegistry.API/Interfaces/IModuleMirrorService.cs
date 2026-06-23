using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IModuleMirrorService
{
    Task<ModuleVersions> GetModuleVersionsAsync(
        string moduleNamespace,
        string name,
        string provider,
        ModuleVersions localVersions,
        CancellationToken cancellationToken = default);

    Task<TerraformModule?> GetModuleAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        TerraformModule? localModule,
        CancellationToken cancellationToken = default);

    Task<string?> GetModuleDownloadPathAsync(
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string? localDownloadPath,
        CancellationToken cancellationToken = default);
}
