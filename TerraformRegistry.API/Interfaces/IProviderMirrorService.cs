using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IProviderMirrorService
{
    Task<ProviderMirrorIndexResponse?> GetProviderIndexAsync(
        string hostname,
        string providerNamespace,
        string type,
        CancellationToken cancellationToken);

    Task<ProviderMirrorVersionResponse?> GetProviderVersionAsync(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        CancellationToken cancellationToken);

    Task<ProviderMirrorPackageDownload?> OpenPackageAsync(
        string hostname,
        string providerNamespace,
        string type,
        string filename,
        IReadOnlyDictionary<string, string[]> query,
        CancellationToken cancellationToken);
}
