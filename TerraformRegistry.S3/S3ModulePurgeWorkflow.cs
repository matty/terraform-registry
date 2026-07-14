using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.S3;

internal sealed class S3ModulePurgeWorkflow(
    IDatabaseService databaseService,
    S3ModuleObjectStore objectStore,
    ILogger logger)
{
    public async Task<bool> PurgeModuleVersionAsync(string @namespace, string name, string provider, string version,
        CancellationToken cancellationToken)
    {
        ModuleStorage? activeModuleStorage;
        ModuleStorage? moduleStorage;

        try
        {
            activeModuleStorage = await databaseService.GetModuleStorageAsync(@namespace, name, provider, version,
                cancellationToken);
            moduleStorage = activeModuleStorage ??
                            await databaseService.GetModuleStorageIncludingDeletedAsync(@namespace, name, provider,
                                version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error reading module row for purge {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        if (moduleStorage == null)
        {
            return false;
        }

        var logicalObjectKey = S3ModuleObjectKeys.CreateLogicalObjectKey(@namespace, name, provider, version);
        var purgeableObjectKeys = await objectStore.CollectPurgeableObjectKeysAsync(logicalObjectKey, moduleStorage,
            cancellationToken);
        if (!purgeableObjectKeys.Success)
        {
            return false;
        }

        return activeModuleStorage != null
            ? await PurgeActiveModuleAsync(activeModuleStorage, moduleStorage, purgeableObjectKeys.ObjectKeys,
                cancellationToken)
            : await PurgeDeletedModuleAsync(moduleStorage, purgeableObjectKeys.ObjectKeys, cancellationToken);
    }

    private async Task<bool> PurgeActiveModuleAsync(
        ModuleStorage activeModuleStorage,
        ModuleStorage moduleStorage,
        IReadOnlyList<string> objectKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await databaseService.RemoveModuleExactAsync(activeModuleStorage, cancellationToken);
            if (!removed)
            {
                RegistryLog.Warning(logger,
                    "Failed to remove exact active module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                    activeModuleStorage.Namespace,
                    activeModuleStorage.Name,
                    activeModuleStorage.Provider,
                    activeModuleStorage.Version);
                return false;
            }

            var deletedObjects = await objectStore.DeletePurgeableObjectKeysAsync(objectKeys, moduleStorage,
                cancellationToken);
            if (deletedObjects)
            {
                return true;
            }

            await TryRestoreActiveModuleSnapshotAsync(activeModuleStorage);
            return false;
        }
        catch (OperationCanceledException)
        {
            await TryRestoreActiveModuleSnapshotAsync(activeModuleStorage);
            throw;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error removing exact active module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                activeModuleStorage.Namespace,
                activeModuleStorage.Name,
                activeModuleStorage.Provider,
                activeModuleStorage.Version);
            return false;
        }
    }

    private async Task<bool> PurgeDeletedModuleAsync(ModuleStorage moduleStorage, IReadOnlyList<string> objectKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await databaseService.RemoveDeletedModuleAsync(
                moduleStorage.Namespace,
                moduleStorage.Name,
                moduleStorage.Provider,
                moduleStorage.Version, cancellationToken);
            if (!removed)
            {
                RegistryLog.Warning(logger,
                    "Failed to remove deleted module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                    moduleStorage.Namespace,
                    moduleStorage.Name,
                    moduleStorage.Provider,
                    moduleStorage.Version);
                return false;
            }

            var deletedObjects = await objectStore.DeletePurgeableObjectKeysAsync(objectKeys, moduleStorage,
                cancellationToken);
            if (deletedObjects)
            {
                return true;
            }

            await TryRestoreDeletedModuleSnapshotAsync(moduleStorage);
            return false;
        }
        catch (OperationCanceledException)
        {
            await TryRestoreDeletedModuleSnapshotAsync(moduleStorage);
            throw;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error removing deleted module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                moduleStorage.Namespace,
                moduleStorage.Name,
                moduleStorage.Provider,
                moduleStorage.Version);
            return false;
        }
    }

    private async Task TryRestoreActiveModuleSnapshotAsync(ModuleStorage module)
    {
        try
        {
            var restored = await databaseService.AddModuleAsync(module);
            if (!restored)
            {
                RegistryLog.Error(logger,
                    "Failed to restore active module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
            }
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error restoring active module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
        }
    }

    private async Task TryRestoreDeletedModuleSnapshotAsync(ModuleStorage module)
    {
        try
        {
            var restored = await databaseService.AddDeletedModuleAsync(module);
            if (!restored)
            {
                RegistryLog.Error(logger,
                    "Failed to restore deleted module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
            }
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error restoring deleted module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
        }
    }
}
