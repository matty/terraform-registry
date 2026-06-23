using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderMirrorRepository
{
    Task<MirrorProviderIndex?> GetProviderIndexAsync(
        string hostname,
        string providerNamespace,
        string type);

    Task UpsertProviderIndexAsync(MirrorProviderIndex providerIndex);

    Task<IReadOnlyList<MirrorProviderPackage>> ListProviderPackagesAsync(
        string? q,
        string? state,
        int limit,
        int offset);

    Task<MirrorProviderPackage?> GetProviderPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch);

    Task UpsertProviderPackageAsync(MirrorProviderPackage package);

    Task MarkProviderPackageFailedAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        string errorMessage,
        int? httpStatusCode = null);
}
