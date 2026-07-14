using System.Globalization;
using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.S3;

public class S3ModuleService : ModuleService
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<S3ModuleService> _logger;
    private readonly S3ModuleObjectStore _objectStore;
    private readonly S3ModulePurgeWorkflow _purgeWorkflow;
    private readonly S3ModuleUploadWorkflow _uploadWorkflow;

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
            throw new ArgumentNullException(nameof(configuration), "S3:BucketName configuration value is required.");
        }

        var region = configuration["S3:Region"];
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentNullException(nameof(configuration), "S3:Region configuration value is required.");
        }

        var presignedUrlExpiryMinutes = ParsePresignedUrlExpiry(configuration, logger);
        var resolvedS3Client = s3Client ?? CreateS3Client(configuration, region, s3ClientFactory);

        _objectStore = new S3ModuleObjectStore(
            resolvedS3Client,
            bucketName,
            presignedUrlExpiryMinutes,
            Uri.TryCreate(configuration["S3:ServiceUrl"], UriKind.Absolute, out var endpoint) &&
            string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase),
            logger);
        _uploadWorkflow = new S3ModuleUploadWorkflow(_databaseService, _objectStore, logger);
        _purgeWorkflow = new S3ModulePurgeWorkflow(_databaseService, _objectStore, logger);

    }

    public override Task InitializeStorageAsync(CancellationToken cancellationToken) =>
        _objectStore.InitializeStorageAsync(cancellationToken);

    public override Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListModulesAsync(request);
    }

    public override Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        return _databaseService.GetModuleAsync(moduleNamespace, name, provider, version);
    }

    public override Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider)
    {
        return _databaseService.GetModuleVersionsAsync(moduleNamespace, name, provider);
    }

    public override async Task<string?> GetModuleDownloadPathAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
        {
            RegistryLog.Warning(_logger,
                "Module {Namespace}/{Name}/{Provider}/{Version} not found in database.",
                moduleNamespace,
                name,
                provider,
                version);
            return null;
        }

        return await _objectStore.GetModuleDownloadPathAsync(moduleStorage, moduleNamespace, name, provider, version);
    }

    public override async Task<Stream?> OpenModulePackageStreamAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
        {
            return null;
        }

        return await _objectStore.OpenModulePackageStreamAsync(moduleStorage, moduleNamespace, name, provider, version);
    }

    protected override Task<bool> UploadModuleAsyncCore(string moduleNamespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata,
        CancellationToken cancellationToken)
    {
        return _uploadWorkflow.UploadModuleAsync(moduleNamespace, name, provider, version, moduleContent, description,
            replace, metadata, cancellationToken);
    }

    public override Task<bool> DeleteModuleVersionAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        return _databaseService.SoftDeleteModuleAsync(moduleNamespace, name, provider, version);
    }

    public override Task<bool> RestoreModuleVersionAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        return _databaseService.RestoreModuleAsync(moduleNamespace, name, provider, version);
    }

    public override Task<bool> PurgeModuleVersionAsync(string moduleNamespace, string name, string provider, string version)
    {
        return _purgeWorkflow.PurgeModuleVersionAsync(moduleNamespace, name, provider, version);
    }

    public override Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListDeletedModulesAsync(request);
    }

    public override Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider,
        string description)
    {
        return _databaseService.UpdateModuleDescriptionAsync(moduleNamespace, name, provider, description);
    }

    public override Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        return _objectStore.CheckStorageAsync();
    }

    private static int ParsePresignedUrlExpiry(
        IConfiguration configuration,
        ILogger<S3ModuleService> logger)
    {
        var configuredPresignedUrlExpiry = configuration["S3:PresignedUrlExpiryMinutes"] ?? "5";
        if (int.TryParse(configuredPresignedUrlExpiry, CultureInfo.InvariantCulture, out var presignedUrlExpiryMinutes)
            && presignedUrlExpiryMinutes > 0)
        {
            return presignedUrlExpiryMinutes;
        }

        RegistryLog.Warning(logger,
            "S3:PresignedUrlExpiryMinutes must be a positive integer, but was configured as {ConfiguredValue}. Defaulting to 5 minutes.",
            configuredPresignedUrlExpiry);
        return 5;
    }

    private static IAmazonS3 CreateS3Client(
        IConfiguration configuration,
        string region,
        IS3ClientFactory? s3ClientFactory)
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
            config.UseHttp = Uri.TryCreate(serviceUrl, UriKind.Absolute, out var endpoint) &&
                             string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }

        return (s3ClientFactory ?? new S3ClientFactory()).Create(
            config,
            configuration["S3:AccessKeyId"],
            configuration["S3:SecretAccessKey"],
            configuration["S3:SessionToken"]);
    }
}
