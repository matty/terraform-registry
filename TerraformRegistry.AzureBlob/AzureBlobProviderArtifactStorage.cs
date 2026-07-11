using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.AzureBlob;

public sealed class AzureBlobProviderArtifactStorage : IProviderArtifactStorage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobProviderArtifactStorage> _logger;
    private readonly int _sasTokenExpiryMinutes;

    public AzureBlobProviderArtifactStorage(
        IConfiguration configuration,
        ILogger<AzureBlobProviderArtifactStorage> logger,
        BlobServiceClient? blobServiceClient = null)
    {
        _logger = logger;
        _containerName = configuration["AzureStorage:ContainerName"]
                         ?? throw new ArgumentNullException(nameof(configuration),
                             "AzureStorage:ContainerName configuration value is required.");

        _sasTokenExpiryMinutes = int.TryParse(configuration["AzureStorage:SasTokenExpiryMinutes"], out var configured)
            ? configured
            : 5;
        if (_sasTokenExpiryMinutes <= 0)
        {
            RegistryLog.Warning(_logger,
                "AzureStorage:SasTokenExpiryMinutes must be a positive integer, but was configured as {ConfiguredValue}. Defaulting to 5 minutes.",
                _sasTokenExpiryMinutes);
            _sasTokenExpiryMinutes = 5;
        }

        BlobServiceClient clientToUse;
        if (blobServiceClient != null)
        {
            clientToUse = blobServiceClient;
        }
        else
        {
            var connectionString = configuration["AzureStorage:ConnectionString"];
            var accountName = configuration["AzureStorage:AccountName"];

            if (string.IsNullOrEmpty(connectionString))
            {
                if (string.IsNullOrEmpty(accountName))
                {
                    const string errorMessage =
                        "Azure Storage AccountName ('AzureStorage:AccountName') is required when connection string is not provided (for Managed Identity).";
                    RegistryLog.Error(_logger, "{ErrorMessage}", errorMessage);
                    throw new ArgumentNullException(nameof(configuration), errorMessage);
                }

                var blobServiceUri = new Uri($"https://{accountName}.blob.core.windows.net");
                clientToUse = new BlobServiceClient(blobServiceUri, new DefaultAzureCredential());
            }
            else
            {
                clientToUse = new BlobServiceClient(connectionString);
            }
        }

        _blobServiceClient = clientToUse;
        _containerClient = clientToUse.GetBlobContainerClient(_containerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task<ProviderArtifactSaveResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        var blobName = GetBlobName(relativePath);
        var blobClient = _containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(content, overwrite: true, cancellationToken);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new ProviderArtifactSaveResult(GetStoragePath(blobName), properties.Value.ContentLength);
    }

    public async Task<string> CreateDownloadUrlAsync(string storagePath, CancellationToken cancellationToken)
    {
        var blobName = GetBlobName(storagePath);
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
            throw new FileNotFoundException("Provider artifact blob was not found.", storagePath);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_sasTokenExpiryMinutes)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        if (blobClient.CanGenerateSasUri)
        {
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
            startsOn,
            sasBuilder.ExpiresOn,
            cancellationToken);
        return blobClient.GenerateUserDelegationSasUri(sasBuilder, delegationKey.Value).ToString();
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        var blobName = GetBlobName(storagePath);
        var blobClient = _containerClient.GetBlobClient(blobName);
        if (!await blobClient.ExistsAsync(cancellationToken)) return null;

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken)
    {
        return await _containerClient.GetBlobClient(GetBlobName(storagePath)).ExistsAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        var response = await _containerClient.GetBlobClient(GetBlobName(storagePath)).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<(bool Healthy, string? Reason)> CheckStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var probe = _containerClient.GetBlobClient($"providers/.health-{Guid.NewGuid():N}");
            await using var content = new MemoryStream([1]);
            await probe.UploadAsync(content, overwrite: true, cancellationToken);
            await probe.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Provider artifact Azure Blob storage health check failed");
            return (false, ex.Message);
        }
    }

    private static string GetBlobName(string storagePath)
    {
        var normalized = ValidateStoragePath(storagePath);
        return $"providers/{normalized}";
    }

    private static string GetStoragePath(string blobName)
    {
        const string prefix = "providers/";
        return blobName.StartsWith(prefix, StringComparison.Ordinal)
            ? blobName[prefix.Length..]
            : blobName;
    }

    private static string ValidateStoragePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
            throw new InvalidOperationException("Provider artifact path escapes storage root.");

        var segments = storagePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("Provider artifact path escapes storage root.");

        return string.Join('/', segments);
    }
}
