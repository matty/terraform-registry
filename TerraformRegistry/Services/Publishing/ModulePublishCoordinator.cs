using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Services.Publishing;

public sealed class ModulePublishCoordinator(
    IModuleService moduleService,
    IModuleExtractionService extractionService,
    WebhookDispatcher webhookDispatcher,
    IAuditService auditService,
    ILogger<ModulePublishCoordinator> logger) : IModulePublishCoordinator
{
    public async Task<bool> PublishAsync(ModulePublishRequest request, CancellationToken cancellationToken)
    {
        var uploaded = await moduleService.UploadModuleAsync(
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            request.ModuleContent,
            request.Description,
            request.Replace,
            request.Metadata);

        if (!uploaded)
            return false;

        webhookDispatcher.FireEvent(
            "module.published",
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            request.Description);

        try
        {
            await extractionService.QueueAsync(new ModuleExtractionRequest(
                    request.Namespace,
                    request.Name,
                    request.Provider,
                    request.Version),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to queue extraction for module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
        }

        _ = auditService.LogAsync(
            request.ActorUserId,
            request.AuditAction,
            "module",
            $"{request.Namespace}/{request.Name}/{request.Provider}/{request.Version}",
            new
            {
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                request.Replace,
                Source = request.Metadata.Source?.Kind
            },
            null);

        logger.LogInformation(
            "Published module {Namespace}/{Name}/{Provider}/{Version} via {SourceKind}",
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            request.Metadata.Source?.Kind ?? "unknown");

        return true;
    }
}
