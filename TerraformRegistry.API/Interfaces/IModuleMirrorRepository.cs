using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IModuleMirrorRepository
{
    Task<MirrorModuleVersions?> GetModuleVersionsAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider);

    Task UpsertModuleVersionsAsync(MirrorModuleVersions moduleVersions);

    Task<IReadOnlyList<MirrorModulePackage>> ListModulePackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset);

    Task<MirrorModulePackage?> GetModulePackageAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version);

    Task UpsertModulePackageAsync(MirrorModulePackage package);

    Task MarkModulePackageFailedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version,
        string errorMessage,
        int? httpStatusCode = null);
}
