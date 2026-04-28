using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IGitHubVcsService
{
    Task<(string Status, string? Reason, string? Version)> HandleWebhookAsync(
        string? signatureHeader,
        string? eventHeader,
        string body);

    Task<SyncVcsSourceResult> SyncSourceAsync(
        Guid sourceId,
        string? requestedTag,
        bool replace,
        string? actorUserId,
        CancellationToken cancellationToken);
}
