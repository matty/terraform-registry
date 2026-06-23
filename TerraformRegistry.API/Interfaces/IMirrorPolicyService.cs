using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IMirrorPolicyService
{
    Task<bool> IsProviderAllowedAsync(
        string hostname,
        string providerNamespace,
        string type,
        string os,
        string arch,
        CancellationToken cancellationToken = default);

    Task<bool> IsModuleAllowedAsync(
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        CancellationToken cancellationToken = default);

    Task<ValidatedMirrorEndpoint> ValidateModuleArchiveUrlAsync(
        string archiveUrl,
        CancellationToken cancellationToken = default);

    Task<ValidatedMirrorEndpoint> ValidateProviderArtifactUrlAsync(
        string artifactUrl,
        CancellationToken cancellationToken = default);
}
