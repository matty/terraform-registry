using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Services.Publishing;

public sealed class ModulePublishCoordinator(
    IModuleService moduleService,
    IModuleExtractionService extractionService,
    WebhookDispatcher webhookDispatcher,
    IAuditService auditService,
    ILogger<ModulePublishCoordinator> logger,
    IArchiveIngestionValidator? archiveValidator = null) : IModulePublishCoordinator
{
    public async Task<bool> PublishAsync(ModulePublishRequest request, CancellationToken cancellationToken)
    {
        if (archiveValidator is not null)
        {
            await using var archive = await archiveValidator.PrepareAsync(request.ModuleContent, cancellationToken);
            await using var content = archive.OpenRead();
            return await PublishValidatedAsync(request, content, cancellationToken);
        }

        return await PublishValidatedAsync(request, request.ModuleContent, cancellationToken);
    }

    private async Task<bool> PublishValidatedAsync(ModulePublishRequest request, Stream content, CancellationToken cancellationToken)
    {
        var uploaded = await moduleService.UploadModuleAsync(
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            content,
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
            RegistryLog.Warning(logger,
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

        RegistryLog.Information(logger,
            "Published module {Namespace}/{Name}/{Provider}/{Version} via {SourceKind}",
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            request.Metadata.Source?.Kind ?? "unknown");

        return true;
    }
}
