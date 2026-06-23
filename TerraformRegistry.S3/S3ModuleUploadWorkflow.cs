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
        ModuleArtifactMetadata? metadata)
    {
        ModuleStorage? existingModule;
        try
        {
            existingModule = await databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        }
        catch (Exception ex)
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

        var logicalObjectKey = S3ModuleObjectKeys.CreateLogicalObjectKey(@namespace, name, provider, version);
        var objectKey = S3ModuleObjectKeys.CreateFinalObjectKey(logicalObjectKey);
        var newModule = new ModuleStorage
        {
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            Description = description,
            FilePath = objectKey,
            PublishedAt = DateTime.UtcNow,
            Dependencies = [],
            Metadata = metadata ?? new ModuleArtifactMetadata()
        };

        var tempKey = S3ModuleObjectKeys.CreateTemporaryObjectKey(objectKey);
        if (!await objectStore.UploadTemporaryObjectAsync(newModule, moduleContent, tempKey))
        {
            return false;
        }

        return await FinalizeUploadAsync(existingModule, newModule, tempKey, replace && existingModule != null);
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
        catch (Exception ex)
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
        bool replacingExisting)
    {
        if (!await objectStore.TryPromoteTemporaryObjectAsync(tempKey, newModule, replacingExisting ? "replace" : "create"))
        {
            await objectStore.TryDeleteObjectAsync(newModule.FilePath, "final");
            await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
            return false;
        }

        if (replacingExisting)
        {
            if (!await TryReplaceExistingModuleSnapshotAsync(existingModule, newModule))
            {
                await objectStore.TryDeleteObjectAsync(newModule.FilePath, "final");
                await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
                return false;
            }

            if (existingModule != null)
            {
                await objectStore.TryDeleteObjectAsync(existingModule.FilePath, "superseded final");
            }

            await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
            return true;
        }

        try
        {
            var added = await databaseService.AddModuleAsync(newModule);
            if (!added)
            {
                await objectStore.TryDeleteObjectAsync(newModule.FilePath, "final");
                await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
                RegistryLog.Error(logger,
                    "Failed to add module {Namespace}/{Name}/{Provider}/{Version} to database after S3 finalization.",
                    newModule.Namespace,
                    newModule.Name,
                    newModule.Provider,
                    newModule.Version);
                return false;
            }
        }
        catch (Exception ex)
        {
            await objectStore.TryDeleteObjectAsync(newModule.FilePath, "final");
            await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
            RegistryLog.Error(logger,
                ex,
                "Error adding module {Namespace}/{Name}/{Provider}/{Version} to database after S3 finalization.",
                newModule.Namespace,
                newModule.Name,
                newModule.Provider,
                newModule.Version);
            return false;
        }

        await objectStore.TryDeleteTemporaryObjectAsync(tempKey);
        return true;
    }

    private async Task<bool> TryReplaceExistingModuleSnapshotAsync(ModuleStorage? existingModule, ModuleStorage newModule)
    {
        if (existingModule == null)
        {
            return false;
        }

        try
        {
            var replaced = await databaseService.ReplaceModuleExactAsync(existingModule, newModule);
            if (replaced)
            {
                return true;
            }

            RegistryLog.Warning(logger,
                "Failed to replace exact existing module row for {Namespace}/{Name}/{Provider}/{Version} after S3 finalization.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
            return false;
        }
        catch (Exception ex)
        {
            RegistryLog.Warning(logger,
                ex,
                "Error replacing exact existing module row for {Namespace}/{Name}/{Provider}/{Version} after S3 finalization.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
            return false;
        }
    }
}
