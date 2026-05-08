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

    public override async Task<Stream?> OpenModulePackageStreamAsync(string @namespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
        {
            return null;
        }

        try
        {
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = moduleStorage.FilePath
            });

            return response.ResponseStream;
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
                "Error opening S3 object stream for module {Namespace}/{Name}/{Provider}/{Version}.",
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
        return PurgeModuleVersionAsyncInternal(@namespace, name, provider, version);
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
        return CheckStorageAsyncInternal();
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

    private async Task<bool> PurgeModuleVersionAsyncInternal(string @namespace, string name, string provider,
        string version)
    {
        ModuleStorage? activeModuleStorage;
        ModuleStorage? moduleStorage;

        try
        {
            activeModuleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
            moduleStorage = activeModuleStorage ??
                            await _databaseService.GetModuleStorageIncludingDeletedAsync(@namespace, name, provider,
                                version);
        }
        catch (Exception ex)
        {
            _logger.LogError(
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

        var logicalObjectKey = CreateLogicalObjectKey(@namespace, name, provider, version);
        var purgeableObjectKeys = await CollectPurgeableObjectKeysAsync(logicalObjectKey, moduleStorage);
        if (!purgeableObjectKeys.Success)
        {
            return false;
        }

        if (activeModuleStorage != null)
        {
            try
            {
                var removed = await _databaseService.RemoveModuleExactAsync(activeModuleStorage);
                if (!removed)
                {
                    _logger.LogWarning(
                        "Failed to remove exact active module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                        activeModuleStorage.Namespace,
                        activeModuleStorage.Name,
                        activeModuleStorage.Provider,
                        activeModuleStorage.Version);
                    return false;
                }

                var deletedObjects = await DeletePurgeableObjectKeysAsync(purgeableObjectKeys.ObjectKeys, moduleStorage);
                if (deletedObjects) return true;

                await TryRestoreActiveModuleSnapshotAsync(activeModuleStorage);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error removing exact active module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                    activeModuleStorage.Namespace,
                    activeModuleStorage.Name,
                    activeModuleStorage.Provider,
                    activeModuleStorage.Version);
                return false;
            }
        }

        try
        {
            var removed = await _databaseService.RemoveDeletedModuleAsync(
                moduleStorage.Namespace,
                moduleStorage.Name,
                moduleStorage.Provider,
                moduleStorage.Version);
            if (!removed)
            {
                _logger.LogWarning(
                    "Failed to remove deleted module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                    moduleStorage.Namespace,
                    moduleStorage.Name,
                    moduleStorage.Provider,
                    moduleStorage.Version);
                return false;
            }

            var deletedObjects = await DeletePurgeableObjectKeysAsync(purgeableObjectKeys.ObjectKeys, moduleStorage);
            if (deletedObjects) return true;

            await TryRestoreDeletedModuleSnapshotAsync(moduleStorage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error removing deleted module row during purge for {Namespace}/{Name}/{Provider}/{Version}.",
                moduleStorage.Namespace,
                moduleStorage.Name,
                moduleStorage.Provider,
                moduleStorage.Version);
            return false;
        }
    }

    private async Task<(bool Healthy, string? Reason)> CheckStorageAsyncInternal()
    {
        try
        {
            await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucketName,
                MaxKeys = 1
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"S3 storage unreachable: {ex.Message}");
        }
    }

    private async Task<bool> UploadModuleAsyncImplInternal(string @namespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata)
    {
        ModuleStorage? existingModule = null;
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

        if (existingModule != null && !replace)
        {
            _logger.LogWarning(
                "Module {Namespace}/{Name}/{Provider}/{Version} already exists in the database.",
                @namespace,
                name,
                provider,
                version);
            return false;
        }

        if (existingModule == null)
        {
            try
            {
                var deletedModule = await _databaseService.GetModuleStorageIncludingDeletedAsync(@namespace, name,
                    provider, version);
                if (deletedModule != null)
                {
                    _logger.LogWarning(
                        "Module {Namespace}/{Name}/{Provider}/{Version} exists in the trash and must be restored or purged before upload.",
                        @namespace,
                        name,
                        provider,
                        version);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error checking deleted module row for {Namespace}/{Name}/{Provider}/{Version}.",
                    @namespace,
                    name,
                    provider,
                    version);
                return false;
            }
        }

        var logicalObjectKey = CreateLogicalObjectKey(@namespace, name, provider, version);
        var objectKey = CreateFinalObjectKey(logicalObjectKey);
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

        return await FinalizeUploadAsync(existingModule, newModule, tempKey, replace && existingModule != null);
    }

    private async Task<bool> FinalizeUploadAsync(ModuleStorage? existingModule, ModuleStorage newModule, string tempKey,
        bool replacingExisting)
    {
        if (!await TryPromoteTemporaryObjectAsync(tempKey, newModule, replacingExisting ? "replace" : "create"))
        {
            await TryDeleteObjectAsync(newModule.FilePath, "final");
            await TryDeleteTemporaryObjectAsync(tempKey);
            return false;
        }

        if (replacingExisting)
        {
            if (!await TryReplaceExistingModuleSnapshotAsync(existingModule, newModule))
            {
                await TryDeleteObjectAsync(newModule.FilePath, "final");
                await TryDeleteTemporaryObjectAsync(tempKey);
                return false;
            }

            if (existingModule != null)
            {
                await TryDeleteObjectAsync(existingModule.FilePath, "superseded final");
            }

            await TryDeleteTemporaryObjectAsync(tempKey);
            return true;
        }

        try
        {
            var added = await _databaseService.AddModuleAsync(newModule);
            if (!added)
            {
                await TryDeleteObjectAsync(newModule.FilePath, "final");
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
            await TryDeleteObjectAsync(newModule.FilePath, "final");
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

    private static string CreateLogicalObjectKey(string @namespace, string name, string provider, string version)
    {
        return $"{@namespace}/{name}-{provider}-{version}.zip";
    }

    private static string CreateFinalObjectKey(string logicalObjectKey)
    {
        return $"{logicalObjectKey}.{Guid.NewGuid():N}";
    }

    private static string CreateTemporaryObjectKey(string objectKey)
    {
        return $"{objectKey}.{Guid.NewGuid():N}.tmp";
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

    private static bool ObjectMetadataMatchesModuleIdentity(MetadataCollection metadata, ModuleStorage module)
    {
        var namespaceName = metadata["namespace"];
        var name = metadata["name"];
        var provider = metadata["provider"];
        var version = metadata["version"];

        return namespaceName != null &&
               name != null &&
               provider != null &&
               version != null &&
               string.Equals(namespaceName, module.Namespace, StringComparison.Ordinal) &&
               string.Equals(name, module.Name, StringComparison.Ordinal) &&
               string.Equals(provider, module.Provider, StringComparison.Ordinal) &&
               string.Equals(version, module.Version, StringComparison.Ordinal);
    }

    private async Task<bool> TryReplaceExistingModuleSnapshotAsync(ModuleStorage? existingModule, ModuleStorage newModule)
    {
        if (existingModule == null)
        {
            return false;
        }

        try
        {
            var replaced = await _databaseService.ReplaceModuleExactAsync(existingModule, newModule);
            if (replaced) return true;

            _logger.LogWarning(
                "Failed to replace exact existing module row for {Namespace}/{Name}/{Provider}/{Version} after S3 finalization.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error replacing exact existing module row for {Namespace}/{Name}/{Provider}/{Version} after S3 finalization.",
                existingModule.Namespace,
                existingModule.Name,
                existingModule.Provider,
                existingModule.Version);
            return false;
        }
    }

    private async Task<(bool Success, IReadOnlyList<string> ObjectKeys)> CollectPurgeableObjectKeysAsync(
        string prefix,
        ModuleStorage module)
    {
        var objectKeys = new List<string>();
        string? continuationToken = null;
        do
        {
            ListObjectsV2Response response;
            try
            {
                response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = prefix,
                    ContinuationToken = continuationToken
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to list S3 objects for purge prefix {Prefix} on module {Namespace}/{Name}/{Provider}/{Version}.",
                    prefix,
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
                return (false, []);
            }

            foreach (var s3Object in response.S3Objects)
            {
                var inspection = await InspectPurgeObjectAsync(s3Object.Key, module);
                if (!inspection.Success)
                {
                    return (false, []);
                }

                if (inspection.ShouldDelete)
                {
                    objectKeys.Add(s3Object.Key);
                }
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (!string.IsNullOrWhiteSpace(continuationToken));

        return (true, objectKeys);
    }

    private async Task<bool> DeletePurgeableObjectKeysAsync(
        IReadOnlyList<string> objectKeys,
        ModuleStorage module)
    {
        foreach (var objectKey in EnumeratePurgeDeletionOrder(objectKeys, module.FilePath))
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
                _logger.LogError(
                    ex,
                    "Failed to delete purgeable S3 object {ObjectKey} for module {Namespace}/{Name}/{Provider}/{Version}.",
                    objectKey,
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> EnumeratePurgeDeletionOrder(
        IReadOnlyList<string> objectKeys,
        string currentFilePath)
    {
        foreach (var objectKey in objectKeys.Where(objectKey =>
                     !string.Equals(objectKey, currentFilePath, StringComparison.Ordinal)))
        {
            yield return objectKey;
        }

        foreach (var objectKey in objectKeys.Where(objectKey =>
                     string.Equals(objectKey, currentFilePath, StringComparison.Ordinal)))
        {
            yield return objectKey;
        }
    }

    private async Task<(bool Success, bool ShouldDelete)> InspectPurgeObjectAsync(string objectKey, ModuleStorage module)
    {
        GetObjectMetadataResponse response;
        try
        {
            response = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (true, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read S3 metadata for purge candidate {ObjectKey} on module {Namespace}/{Name}/{Provider}/{Version}.",
                objectKey,
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
            return (false, false);
        }

        if (!ObjectMetadataMatchesModuleIdentity(response.Metadata, module))
        {
            return (true, false);
        }

        var publishedAtValue = response.Metadata["publishedAt"];
        if (string.IsNullOrWhiteSpace(publishedAtValue) ||
            !DateTime.TryParse(
                publishedAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var objectPublishedAt))
        {
            _logger.LogWarning(
                "Skipping purge candidate {ObjectKey} for module {Namespace}/{Name}/{Provider}/{Version} because its metadata is missing a parseable publishedAt value.",
                objectKey,
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
            return (false, false);
        }

        return (true, objectPublishedAt <= module.PublishedAt);
    }

    private async Task TryRestoreActiveModuleSnapshotAsync(ModuleStorage module)
    {
        try
        {
            var restored = await _databaseService.AddModuleAsync(module);
            if (!restored)
            {
                _logger.LogError(
                    "Failed to restore active module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
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
            var restored = await _databaseService.AddDeletedModuleAsync(module);
            if (!restored)
            {
                _logger.LogError(
                    "Failed to restore deleted module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error restoring deleted module row during purge rollback for {Namespace}/{Name}/{Provider}/{Version}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
        }
    }
}
