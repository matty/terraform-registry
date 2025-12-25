using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.AzureBlob;

public class AzureBlobProviderStorageService : IProviderStorageService
{
    private readonly BlobContainerClient _containerClient;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobProviderStorageService> _logger;
    private readonly int _sasTokenExpiryMinutes;

    public AzureBlobProviderStorageService(
        IConfiguration configuration,
        ILogger<AzureBlobProviderStorageService> logger,
        BlobServiceClient? blobServiceClient = null)
    {
        _logger = logger;

        // Get Azure Storage configuration values
        _containerName = configuration["AzureStorage:ContainerName"]
                         ?? throw new ArgumentNullException("AzureStorage:ContainerName",
                             "Azure Storage container name is required.");

        _sasTokenExpiryMinutes = int.Parse(configuration["AzureStorage:SasTokenExpiryMinutes"] ?? "5");
        if (_sasTokenExpiryMinutes <= 0)
        {
            _logger.LogWarning(
                "AzureStorage:SasTokenExpiryMinutes must be a positive integer, but was configured as {ConfiguredValue}. Defaulting to 5 minutes.",
                _sasTokenExpiryMinutes);
            _sasTokenExpiryMinutes = 5;
        }

        BlobServiceClient clientToUse;

        if (blobServiceClient != null)
        {
            _logger.LogInformation("Using provided BlobServiceClient instance.");
            clientToUse = blobServiceClient;
        }
        else
        {
            _logger.LogInformation("BlobServiceClient not provided; attempting to create one based on configuration.");
            // Get Azure Storage connection settings from configuration
            var connectionString = configuration["AzureStorage:ConnectionString"];
            var accountName = configuration["AzureStorage:AccountName"];

            // Initialize Azure Blob Storage clients
            if (string.IsNullOrEmpty(connectionString))
            {
                if (string.IsNullOrEmpty(accountName))
                {
                    const string errorMessage =
                        "Azure Storage AccountName ('AzureStorage:AccountName') is required when connection string is not provided (for Managed Identity).";
                    _logger.LogError(errorMessage);
                    throw new ArgumentNullException("AzureStorage:AccountName", errorMessage);
                }

                _logger.LogInformation(
                    "Azure Storage connection string not found. Attempting to use Managed Identity for account: {AccountName}.",
                    accountName);
                // Use Managed Identity
                var blobServiceUri = new Uri($"https://{accountName}.blob.core.windows.net");
                clientToUse = new BlobServiceClient(blobServiceUri, new DefaultAzureCredential());
            }
            else
            {
                _logger.LogInformation("Using Azure Storage connection string to create BlobServiceClient.");
                clientToUse = new BlobServiceClient(connectionString);
            }
        }

        // Initialize Azure Blob Storage container client
        _containerClient = clientToUse.GetBlobContainerClient(_containerName);

        // Ensure container exists
        _containerClient.CreateIfNotExists();
    }

    public async Task<string> UploadProviderAsync(string @namespace, string type, string version, string os, string arch, Stream stream)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/{os}_{arch}.zip";
        var blobClient = _containerClient.GetBlobClient(blobPath);

        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            Metadata = new Dictionary<string, string>
            {
                { "namespace", @namespace },
                { "type", type },
                { "version", version },
                { "os", os },
                { "arch", arch }
            }
        });

        // We return the blob path to be stored in DB (or valid URL if public).
        // The DB expects 'download_url' in current implementation.
        // For Azure Blob, usually we want a SAS URL generated on the fly (GetProviderDownloadUrlAsync).
        // So here we return the blob path identifier.
        // Wait, if I return blob path, the DB stores it as 'download_url'.
        // My 'GetProviderPackage' calls 'GetProviderDownloadUrlAsync' or relies on DB?
        // My 'ProviderHandlers' calls 'providerService.GetProviderPackageAsync'.
        // 'ProviderService' calls '_db.GetProviderPackageAsync'.
        // The DB returns whatever string is in 'download_url' column.

        // ISSUE: If I store "providers/namespace/..." in DB, the 'download_url' field in JSON response will be that string, which is not a valid URL.
        // SOLUTION: The 'ProviderService.GetProviderPackageAsync' should intercept the DB result and transform it if needed.
        // Or I store a placeholder/flag in DB.
        // Let's modify 'ProviderService.GetProviderPackageAsync' to use 'IProviderStorageService.GetProviderDownloadUrlAsync' if needed.

        return blobPath;
    }

    public async Task<string?> GetProviderDownloadUrlAsync(string @namespace, string type, string version, string os, string arch)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/{os}_{arch}.zip";
        return await GenerateSasToken(blobPath);
    }

    public async Task UploadShasumsAsync(string @namespace, string type, string version, Stream stream)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/SHA256SUMS";
        var blobClient = _containerClient.GetBlobClient(blobPath);
        await blobClient.UploadAsync(stream, true);
    }

    public async Task UploadShasumsSigAsync(string @namespace, string type, string version, Stream stream)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/SHA256SUMS.sig";
        var blobClient = _containerClient.GetBlobClient(blobPath);
        await blobClient.UploadAsync(stream, true);
    }

    public async Task<string?> GetShasumsDownloadUrlAsync(string @namespace, string type, string version)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/SHA256SUMS";
        return await GenerateSasToken(blobPath);
    }

    public async Task<string?> GetShasumsSigDownloadUrlAsync(string @namespace, string type, string version)
    {
        var blobPath = $"providers/{@namespace}/{type}/{version}/SHA256SUMS.sig";
        return await GenerateSasToken(blobPath);
    }

    public Task<Stream?> GetFileStreamAsync(string relativePath)
    {
        // Azure Blob storage does not support direct stream serving via this API logic.
        // It relies on Redirect URLs.
        return Task.FromResult<Stream?>(null);
    }

    private async Task<string?> GenerateSasToken(string blobPath)
    {
        var blobClient = _containerClient.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync()) return null;

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerName,
            BlobName = blobPath,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_sasTokenExpiryMinutes)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder).ToString();
    }
}
