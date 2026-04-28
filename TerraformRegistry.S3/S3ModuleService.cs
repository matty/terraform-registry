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
        return Task.FromResult(false);
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
}
