using System.Globalization;
using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.S3;

public class S3ModuleService : ModuleService
{
    private readonly string _bucketName;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<S3ModuleService> _logger;
    private readonly int _presignedUrlExpiryMinutes;
    private readonly IAmazonS3 _s3Client;

    public S3ModuleService(
        IConfiguration configuration,
        IDatabaseService databaseService,
        ILogger<S3ModuleService> logger,
        IAmazonS3? s3Client = null,
        IS3ClientFactory? s3ClientFactory = null)
    {
        _databaseService = databaseService;
        _logger = logger;

        var bucketName = configuration["S3:BucketName"];
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new ArgumentNullException("S3:BucketName", "S3 bucket name is required.");
        }

        _bucketName = bucketName;

        var region = configuration["S3:Region"];
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentNullException("S3:Region", "S3 region is required.");
        }

        var configuredPresignedUrlExpiry = configuration["S3:PresignedUrlExpiryMinutes"] ?? "5";
        if (!int.TryParse(configuredPresignedUrlExpiry, CultureInfo.InvariantCulture, out _presignedUrlExpiryMinutes)
            || _presignedUrlExpiryMinutes <= 0)
        {
            _logger.LogWarning(
                "S3:PresignedUrlExpiryMinutes must be a positive integer, but was configured as {ConfiguredValue}. Defaulting to 5 minutes.",
                configuredPresignedUrlExpiry);
            _presignedUrlExpiryMinutes = 5;
        }

        if (s3Client != null)
        {
            _s3Client = s3Client;
        }
        else
        {
            var config = new AmazonS3Config
            {
                AuthenticationRegion = region,
                RegionEndpoint = RegionEndpoint.GetBySystemName(region),
                ForcePathStyle = bool.TryParse(configuration["S3:ForcePathStyle"], out var forcePathStyle) &&
                                 forcePathStyle
            };

            var serviceUrl = configuration["S3:ServiceUrl"];
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                config.ServiceURL = serviceUrl;
            }

            _s3Client = (s3ClientFactory ?? new S3ClientFactory()).Create(
                config,
                configuration["S3:AccessKeyId"],
                configuration["S3:SecretAccessKey"],
                configuration["S3:SessionToken"]);
        }

        TryPrimeStorage();
    }

    public override Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListModulesAsync(request);
    }

    public override Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        return _databaseService.GetModuleAsync(@namespace, name, provider, version);
    }

    public override Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        return _databaseService.GetModuleVersionsAsync(@namespace, name, provider);
    }

    public override async Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
        {
            _logger.LogWarning(
                "Module {Namespace}/{Name}/{Provider}/{Version} not found in database.",
                @namespace,
                name,
                provider,
                version);
            return null;
        }

        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = moduleStorage.FilePath
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Module {Namespace}/{Name}/{Provider}/{Version} exists in database but object {ObjectKey} was not found in S3.",
                @namespace,
                name,
                provider,
                version,
                moduleStorage.FilePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error checking S3 object for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return null;
        }

        try
        {
            return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = moduleStorage.FilePath,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(_presignedUrlExpiryMinutes)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error generating pre-signed URL for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return null;
        }
    }

    protected override Task<bool> UploadModuleAsyncImpl(string @namespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata)
    {
        return UploadModuleAsyncImplInternal(@namespace, name, provider, version, moduleContent, description, replace,
            metadata);
    }

    public override Task<bool> DeleteModuleVersionAsync(string @namespace, string name, string provider,
        string version)
    {
        return _databaseService.SoftDeleteModuleAsync(@namespace, name, provider, version);
    }

    public override Task<bool> RestoreModuleVersionAsync(string @namespace, string name, string provider,
        string version)
    {
        return _databaseService.RestoreModuleAsync(@namespace, name, provider, version);
    }

    public override Task<bool> PurgeModuleVersionAsync(string @namespace, string name, string provider, string version)
    {
        return Task.FromResult(false);
    }

    public override Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListDeletedModulesAsync(request);
    }

    public override Task<bool> UpdateModuleDescriptionAsync(string @namespace, string name, string provider,
        string description)
    {
        return _databaseService.UpdateModuleDescriptionAsync(@namespace, name, provider, description);
    }

    public override Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        return Task.FromResult((true, (string?)null));
    }

    private void TryPrimeStorage()
    {
        try
        {
            _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucketName,
                MaxKeys = 1
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reach S3 bucket '{BucketName}' during startup.", _bucketName);
        }
    }

    private async Task<bool> UploadModuleAsyncImplInternal(string @namespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata)
    {
        var objectKey = $"{@namespace}/{name}-{provider}-{version}.zip";
        var objectExists = false;

        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            });
            objectExists = true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            objectExists = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error checking S3 object for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        if (objectExists && !replace)
        {
            _logger.LogWarning(
                "Module {Namespace}/{Name}/{Provider}/{Version} already exists in S3.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        ModuleStorage? existingModule = null;
        if (replace)
        {
            try
            {
                existingModule = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error reading existing module row for {Namespace}/{Name}/{Provider}/{Version}.",
                    @namespace,
                    name,
                    provider,
                    version);
                return false;
            }
        }

        var newModule = new ModuleStorage
        {
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            Description = description,
            FilePath = objectKey,
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };

        var tempKey = CreateTemporaryObjectKey(objectKey);

        try
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = tempKey,
                InputStream = moduleContent,
                AutoCloseStream = false
            };
            AddModuleMetadata(putRequest.Metadata, newModule);
            await _s3Client.PutObjectAsync(putRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error uploading temporary S3 object for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        var isReplacingExisting = replace && (objectExists || existingModule != null);

        if (isReplacingExisting)
        {
            return await FinalizeReplaceUploadAsync(existingModule, newModule, tempKey, objectExists);
        }

        return await FinalizeCreateUploadAsync(newModule, tempKey);
    }

    private async Task<bool> FinalizeCreateUploadAsync(ModuleStorage newModule, string tempKey)
    {
        if (!await TryPromoteTemporaryObjectAsync(tempKey, newModule, "create"))
        {
            await TryDeleteTemporaryObjectAsync(tempKey);
            return false;
        }

        try
        {
            var added = await _databaseService.AddModuleAsync(newModule);
            if (!added)
            {
                await TryDeleteFinalObjectIfMatchesModuleAsync(newModule);
                await TryDeleteTemporaryObjectAsync(tempKey);
                _logger.LogError(
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
            await TryDeleteFinalObjectIfMatchesModuleAsync(newModule);
            await TryDeleteTemporaryObjectAsync(tempKey);
            _logger.LogError(
                ex,
                "Error adding module {Namespace}/{Name}/{Provider}/{Version} to database after S3 finalization.",
                newModule.Namespace,
                newModule.Name,
                newModule.Provider,
                newModule.Version);
            return false;
        }

        await TryDeleteTemporaryObjectAsync(tempKey);
        return true;
    }

    private async Task<bool> FinalizeReplaceUploadAsync(ModuleStorage? existingModule, ModuleStorage newModule,
        string tempKey, bool objectExists)
    {
        string? backupKey = null;

        if (objectExists)
        {
            backupKey = CreateBackupObjectKey(newModule.FilePath);

            try
            {
                await _s3Client.CopyObjectAsync(new CopyObjectRequest
                {
                    SourceBucket = _bucketName,
                    SourceKey = newModule.FilePath,
                    DestinationBucket = _bucketName,
                    DestinationKey = backupKey
                });
            }
            catch (Exception ex)
            {
                await TryDeleteTemporaryObjectAsync(tempKey);
                _logger.LogError(
                    ex,
                    "Error backing up existing S3 object for module {Namespace}/{Name}/{Provider}/{Version}.",
                    newModule.Namespace,
                    newModule.Name,
                    newModule.Provider,
                    newModule.Version);
                return false;
            }

            try
            {
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = newModule.FilePath
                });
            }
            catch (Exception ex)
            {
                await TryDeleteTemporaryObjectAsync(tempKey);
                await TryDeleteBackupObjectAsync(backupKey);
                _logger.LogError(
                    ex,
                    "Error deleting existing S3 object for module {Namespace}/{Name}/{Provider}/{Version} before replacement.",
                    newModule.Namespace,
                    newModule.Name,
                    newModule.Provider,
                    newModule.Version);
                return false;
            }
        }

        await TryRemoveExistingModuleSnapshotAsync(existingModule);

        if (!await TryPromoteTemporaryObjectAsync(tempKey, newModule, "replace"))
        {
            if (await TryRestoreObjectFromBackupAsync(backupKey, newModule.FilePath))
            {
                await TryRestoreModuleRowAsync(existingModule);
            }
            await TryDeleteTemporaryObjectAsync(tempKey);
            await TryDeleteBackupObjectAsync(backupKey);
            return false;
        }

        try
        {
            var added = await _databaseService.AddModuleAsync(newModule);
            if (!added)
            {
                await TryDeleteFinalObjectIfMatchesModuleAsync(newModule);
                if (await TryRestoreObjectFromBackupAsync(backupKey, newModule.FilePath))
                {
                    await TryRestoreModuleRowAsync(existingModule);
                }
                await TryDeleteTemporaryObjectAsync(tempKey);
                await TryDeleteBackupObjectAsync(backupKey);
                _logger.LogError(
                    "Failed to add replacement module row for {Namespace}/{Name}/{Provider}/{Version}.",
                    newModule.Namespace,
                    newModule.Name,
                    newModule.Provider,
                    newModule.Version);
                return false;
            }
        }
        catch (Exception ex)
        {
            await TryDeleteFinalObjectIfMatchesModuleAsync(newModule);
            if (await TryRestoreObjectFromBackupAsync(backupKey, newModule.FilePath))
            {
                await TryRestoreModuleRowAsync(existingModule);
            }
            await TryDeleteTemporaryObjectAsync(tempKey);
            await TryDeleteBackupObjectAsync(backupKey);
            _logger.LogError(
                ex,
                "Error adding replacement module row for {Namespace}/{Name}/{Provider}/{Version}.",
                newModule.Namespace,
                newModule.Name,
                newModule.Provider,
                newModule.Version);
            return false;
        }

        await TryDeleteTemporaryObjectAsync(tempKey);
        await TryDeleteBackupObjectAsync(backupKey);
        return true;
    }

    private static string CreateTemporaryObjectKey(string objectKey)
    {
        return $"{objectKey}.{Guid.NewGuid():N}.tmp";
    }

    private static string CreateBackupObjectKey(string objectKey)
    {
        return $"{objectKey}.{Guid.NewGuid():N}.bak";
    }

    private static void AddModuleMetadata(MetadataCollection metadata, ModuleStorage module)
    {
        metadata["namespace"] = module.Namespace;
        metadata["name"] = module.Name;
        metadata["provider"] = module.Provider;
        metadata["version"] = module.Version;
        metadata["description"] = module.Description;
        metadata["publishedAt"] = module.PublishedAt.ToString("o", CultureInfo.InvariantCulture);
    }

    private async Task TryDeleteTemporaryObjectAsync(string tempKey)
    {
        await TryDeleteObjectAsync(tempKey, "temporary");
    }

    private async Task TryDeleteBackupObjectAsync(string? backupKey)
    {
        if (string.IsNullOrWhiteSpace(backupKey))
        {
            return;
        }

        await TryDeleteObjectAsync(backupKey, "backup");
    }

    private async Task TryDeleteObjectAsync(string objectKey, string objectType)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {ObjectType} S3 object {ObjectKey}.", objectType, objectKey);
        }
    }

    private async Task<bool> TryPromoteTemporaryObjectAsync(string tempKey, ModuleStorage module, string operation)
    {
        try
        {
            await _s3Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = tempKey,
                DestinationBucket = _bucketName,
                DestinationKey = module.FilePath,
                IfNoneMatch = "*"
            });
            return true;
        }
        catch (Exception ex)
        {
            if (await FinalObjectMatchesModuleAsync(module))
            {
                _logger.LogWarning(
                    ex,
                    "S3 finalization for module {Namespace}/{Name}/{Provider}/{Version} during {Operation} reported an error, but the final object metadata matches the uploaded module. Continuing.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version,
                    operation);
                return true;
            }

            _logger.LogError(
                ex,
                "Error finalizing module {Namespace}/{Name}/{Provider}/{Version} from temporary S3 object during {Operation}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version,
                operation);
            return false;
        }
    }

    private async Task<bool> FinalObjectMatchesModuleAsync(ModuleStorage module)
    {
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = module.FilePath
            });
            return ObjectMetadataMatchesModule(response.Metadata, module);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error checking final S3 metadata for module {Namespace}/{Name}/{Provider}/{Version}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
            return false;
        }
    }

    private async Task<bool> TryDeleteFinalObjectIfMatchesModuleAsync(ModuleStorage module)
    {
        if (!await FinalObjectMatchesModuleAsync(module))
        {
            _logger.LogWarning(
                "Skipped deleting final S3 object for module {Namespace}/{Name}/{Provider}/{Version} because the stored metadata no longer matches this upload attempt.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
            return false;
        }

        await TryDeleteObjectAsync(module.FilePath, "final");
        return true;
    }

    private static bool ObjectMetadataMatchesModule(MetadataCollection metadata, ModuleStorage module)
    {
        var namespaceName = metadata["namespace"];
        var name = metadata["name"];
        var provider = metadata["provider"];
        var version = metadata["version"];
        var description = metadata["description"];
        var publishedAt = metadata["publishedAt"];

        return namespaceName != null &&
               name != null &&
               provider != null &&
               version != null &&
               description != null &&
               publishedAt != null &&
               string.Equals(namespaceName, module.Namespace, StringComparison.Ordinal) &&
               string.Equals(name, module.Name, StringComparison.Ordinal) &&
               string.Equals(provider, module.Provider, StringComparison.Ordinal) &&
               string.Equals(version, module.Version, StringComparison.Ordinal) &&
               string.Equals(description, module.Description, StringComparison.Ordinal) &&
               string.Equals(publishedAt, module.PublishedAt.ToString("o", CultureInfo.InvariantCulture),
                   StringComparison.Ordinal);
    }

    private async Task TryRemoveExistingModuleSnapshotAsync(ModuleStorage? existingModule)
    {
        if (existingModule == null)
        {
            return;
        }

        try
        {
            var removed = await _databaseService.RemoveModuleExactAsync(existingModule);
            if (!removed)
            {
                _logger.LogWarning(
                    "Failed to remove exact existing module row for {Namespace}/{Name}/{Provider}/{Version} before replacement; continuing.",
                    existingModule.Namespace,
                    existingModule.Name,
                    existingModule.Provider,
                    existingModule.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error removing exact existing module row for {Namespace}/{Name}/{Provider}/{Version} before replacement; continuing.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
        }
    }

    private async Task TryRemoveExactModuleRowAsync(ModuleStorage module, string context)
    {
        try
        {
            var removed = await _databaseService.RemoveModuleExactAsync(module);
            if (!removed)
            {
                _logger.LogError(
                    "Failed to remove exact module row for {Namespace}/{Name}/{Provider}/{Version} {Context}.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version,
                    context);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error removing exact module row for {Namespace}/{Name}/{Provider}/{Version} {Context}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version,
                context);
        }
    }

    private async Task TryRestoreModuleRowAsync(ModuleStorage? existingModule)
    {
        if (existingModule == null)
        {
            return;
        }

        try
        {
            var restored = await _databaseService.AddModuleAsync(existingModule);
            if (!restored)
            {
                _logger.LogError(
                    "Failed to restore previous module row for {Namespace}/{Name}/{Provider}/{Version}.",
                    existingModule.Namespace,
                    existingModule.Name,
                    existingModule.Provider,
                    existingModule.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error restoring previous module row for {Namespace}/{Name}/{Provider}/{Version}.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
        }
    }

    private async Task<bool> TryRestoreObjectFromBackupAsync(string? backupKey, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(backupKey))
        {
            return false;
        }

        try
        {
            await _s3Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = backupKey,
                DestinationBucket = _bucketName,
                DestinationKey = objectKey,
                IfNoneMatch = "*"
            });
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning(
                "Skipped restoring S3 object {ObjectKey} from backup {BackupKey} because another writer recreated the final key first.",
                objectKey,
                backupKey);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore S3 object {ObjectKey} from backup {BackupKey}.", objectKey,
                backupKey);
            return false;
        }
    }
}
