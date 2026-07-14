using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.S3;

internal sealed class S3ModuleUploadWorkflow(
    IDatabaseService databaseService,
    S3ModuleObjectStore objectStore,
    ILogger logger)
{
    public async Task<bool> UploadModuleAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        Stream moduleContent,
        string description,
        bool replace,
        ModuleArtifactMetadata? metadata,
        CancellationToken cancellationToken)
    {
        ModuleStorage? existingModule;
        try
        {
            existingModule = await databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(logger,
                ex,
                "Error reading existing module row for {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        if (existingModule != null && !replace)
        {
            RegistryLog.Warning(logger,
                "Module {Namespace}/{Name}/{Provider}/{Version} already exists in the database.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        if (existingModule == null && !await EnsureNoDeletedModuleBlocksUploadAsync(@namespace, name, provider, version))
        {
            return false;
        }

        var logicalObjectKey = S3ModuleObjectKeys.CreateLogicalObjectKey(@namespace, name, provider, version,
            ModuleArchiveFormat.GetFileSuffix(metadata));
        var objectKey = S3ModuleObjectKeys.CreateFinalObjectKey(logicalObjectKey);
        var now = DateTime.UtcNow;
        var attemptId = Guid.NewGuid();
        var newModule = new ModuleStorage
        {
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            Description = description,
            FilePath = objectKey,
            PublishedAt = now,
            Dependencies = [],
            Metadata = metadata ?? new ModuleArtifactMetadata()
        };

        var tempKey = S3ModuleObjectKeys.CreateTemporaryObjectKey(objectKey);
        var attempt = new ModulePublicationAttempt
        {
            Id = attemptId,
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            State = ModulePublicationAttemptState.Staged,
            StagingKey = tempKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = attemptId,
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            State = ModuleExtractionJobState.Staged,
            CreatedAt = now,
            UpdatedAt = now
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await databaseService.CreatePublicationAttemptWithExtractionJobAsync(attempt, job, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(logger, ex,
                "Error creating staged S3 publication for {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace, name, provider, version);
            return false;
        }

        try
        {
            if (!await objectStore.UploadTemporaryObjectAsync(newModule, moduleContent, tempKey, cancellationToken))
            {
                await TryFailPublicationAsync(attemptId, "Temporary S3 upload failed.");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedPublicationAsync(newModule, tempKey, attemptId, "Publication canceled.");
            throw;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await FinalizeUploadAsync(existingModule, newModule, tempKey, attempt, replace && existingModule != null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedPublicationAsync(newModule, tempKey, attemptId, "Publication canceled.");
            throw;
        }
    }

    private async Task<bool> EnsureNoDeletedModuleBlocksUploadAsync(
        string @namespace,
        string name,
        string provider,
        string version)
    {
        try
        {
            var deletedModule = await databaseService.GetModuleStorageIncludingDeletedAsync(@namespace, name,
                provider, version);
            if (deletedModule == null)
            {
                return true;
            }

            RegistryLog.Warning(logger,
                "Module {Namespace}/{Name}/{Provider}/{Version} exists in the trash and must be restored or purged before upload.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(logger,
                ex,
                "Error checking deleted module row for {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }
    }

    private async Task<bool> FinalizeUploadAsync(
        ModuleStorage? existingModule,
        ModuleStorage newModule,
        string tempKey,
        ModulePublicationAttempt attempt,
        bool replacingExisting,
        CancellationToken cancellationToken)
    {
        if (!await objectStore.TryPromoteTemporaryObjectAsync(tempKey, newModule, replacingExisting ? "replace" : "create",
                cancellationToken))
        {
            await CleanupFailedPublicationAsync(newModule, tempKey, attempt.Id, "S3 finalization failed.");
            return false;
        }

        bool committed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            committed = await databaseService.TryCommitStagedPublicationAsync(attempt, newModule, existingModule,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(logger, ex,
                "Error committing staged S3 publication for {Namespace}/{Name}/{Provider}/{Version}.",
                newModule.Namespace, newModule.Name, newModule.Provider, newModule.Version);
            await CleanupFailedPublicationAsync(newModule, tempKey, attempt.Id, ex.Message);
            return false;
        }

        if (!committed)
        {
            await CleanupFailedPublicationAsync(newModule, tempKey, attempt.Id,
                "Catalog changed before publication could commit.");
            return false;
        }

        await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
        if (replacingExisting && existingModule != null)
        {
            await objectStore.TryDeleteObjectAsync(existingModule.FilePath, "superseded final");
        }
        return true;
    }

    private async Task CleanupFailedPublicationAsync(
        ModuleStorage newModule,
        string tempKey,
        Guid attemptId,
        string reason)
    {
        await objectStore.TryDeleteObjectAsync(newModule.FilePath, "final");
        await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
        await TryFailPublicationAsync(attemptId, reason);
    }

    private async Task TryFailPublicationAsync(Guid attemptId, string reason)
    {
        try { await databaseService.TryFailStagedPublicationAsync(attemptId, reason, CancellationToken.None); }
        catch (Exception ex) when (ex is not OperationCanceledException) { RegistryLog.Error(logger, ex, "Failed to mark staged S3 publication {AttemptId} as failed.", attemptId); }
    }
}
