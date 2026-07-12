using System.Globalization;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.S3;

internal sealed class S3ModuleObjectStore(
    IAmazonS3 s3Client,
    string bucketName,
    int presignedUrlExpiryMinutes,
    bool useHttp,
    ILogger logger)
{
    public async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            MaxKeys = 1
        }, cancellationToken);
    }

    public async Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        try
        {
            await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucketName,
                MaxKeys = 1
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"S3 storage unreachable: {ex.Message}");
        }
    }

    public async Task<string?> GetModuleDownloadPathAsync(
        ModuleStorage moduleStorage,
        string @namespace,
        string name,
        string provider,
        string version)
    {
        try
        {
            await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = moduleStorage.FilePath
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RegistryLog.Warning(logger,
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
            RegistryLog.Error(logger,
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
            return s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = moduleStorage.FilePath,
                Verb = HttpVerb.GET,
                Protocol = useHttp ? Protocol.HTTP : Protocol.HTTPS,
                Expires = DateTime.UtcNow.AddMinutes(presignedUrlExpiryMinutes)
            });
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error generating pre-signed URL for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return null;
        }
    }

    public async Task<Stream?> OpenModulePackageStreamAsync(
        ModuleStorage moduleStorage,
        string @namespace,
        string name,
        string provider,
        string version)
    {
        try
        {
            var response = await s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = moduleStorage.FilePath
            });

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RegistryLog.Warning(logger,
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
            RegistryLog.Error(logger,
                ex,
                "Error opening S3 object stream for module {Namespace}/{Name}/{Provider}/{Version}.",
                @namespace,
                name,
                provider,
                version);
            return null;
        }
    }

    public async Task<bool> UploadTemporaryObjectAsync(ModuleStorage module, Stream moduleContent, string tempKey)
    {
        try
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = tempKey,
                InputStream = moduleContent,
                AutoCloseStream = false
            };
            AddModuleMetadata(putRequest.Metadata, module);
            await s3Client.PutObjectAsync(putRequest);
            return true;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
                ex,
                "Error uploading temporary S3 object for module {Namespace}/{Name}/{Provider}/{Version}.",
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);
            return false;
        }
    }

    public Task TryDeleteTemporaryObjectAsync(string tempKey)
    {
        return TryDeleteObjectAsync(tempKey, "temporary");
    }

    public async Task TryDeleteObjectAsync(string objectKey, string objectType)
    {
        try
        {
            await s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey
            });
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Failed to delete {ObjectType} S3 object {ObjectKey}.", objectType, objectKey);
        }
    }

    public async Task<bool> TryPromoteTemporaryObjectAsync(string tempKey, ModuleStorage module, string operation)
    {
        try
        {
            await s3Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = tempKey,
                DestinationBucket = bucketName,
                DestinationKey = module.FilePath,
                IfNoneMatch = "*"
            });
            return true;
        }
        catch (Exception ex)
        {
            if (await FinalObjectMatchesModuleAsync(module))
            {
                RegistryLog.Warning(logger,
                    ex,
                    "S3 finalization for module {Namespace}/{Name}/{Provider}/{Version} during {Operation} reported an error, but the final object metadata matches the uploaded module. Continuing.",
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version,
                    operation);
                return true;
            }

            RegistryLog.Error(logger,
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

    public async Task<(bool Success, IReadOnlyList<string> ObjectKeys)> CollectPurgeableObjectKeysAsync(
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
                response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    Prefix = prefix,
                    ContinuationToken = continuationToken
                });
            }
            catch (Exception ex)
            {
                RegistryLog.Error(logger,
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

    public async Task<bool> DeletePurgeableObjectKeysAsync(
        IReadOnlyList<string> objectKeys,
        ModuleStorage module)
    {
        foreach (var objectKey in EnumeratePurgeDeletionOrder(objectKeys, module.FilePath))
        {
            try
            {
                await s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                });
            }
            catch (Exception ex)
            {
                RegistryLog.Error(logger,
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

    private async Task<bool> FinalObjectMatchesModuleAsync(ModuleStorage module)
    {
        try
        {
            var response = await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
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
            RegistryLog.Error(logger,
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
            response = await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = objectKey
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (true, false);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger,
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
            RegistryLog.Warning(logger,
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

    private static void AddModuleMetadata(MetadataCollection metadata, ModuleStorage module)
    {
        metadata["namespace"] = module.Namespace;
        metadata["name"] = module.Name;
        metadata["provider"] = module.Provider;
        metadata["version"] = module.Version;
        metadata["description"] = module.Description;
        metadata["publishedAt"] = module.PublishedAt.ToString("o", CultureInfo.InvariantCulture);
    }
}
