using System.Globalization;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.AzureBlob;

// Added for Managed Identity

/// <summary>
///     Implementation of a module service using Azure Blob Storage
/// </summary>
public class AzureBlobModuleService : ModuleService
{
    private readonly BlobContainerClient _containerClient;
    private readonly string _containerName;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<AzureBlobModuleService> _logger;
    private readonly int _sasTokenExpiryMinutes;

    public AzureBlobModuleService(
        IConfiguration configuration,
        IDatabaseService databaseService,
        ILogger<AzureBlobModuleService> logger,
        BlobServiceClient? blobServiceClient = null)
    {
        _databaseService = databaseService;
        _logger = logger;

        // Get Azure Storage configuration values
        _containerName = configuration["AzureStorage:ContainerName"]
                         ?? throw new ArgumentNullException(nameof(configuration),
                             "AzureStorage:ContainerName configuration value is required.");

        _sasTokenExpiryMinutes = int.Parse(configuration["AzureStorage:SasTokenExpiryMinutes"] ?? "5", CultureInfo.InvariantCulture);
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
            RegistryLog.Information(_logger, "Using provided BlobServiceClient instance.");
            clientToUse = blobServiceClient;
        }
        else
        {
            RegistryLog.Information(_logger, "BlobServiceClient not provided; attempting to create one based on configuration.");
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
                    RegistryLog.Error(_logger, "{ErrorMessage}", errorMessage);
                    throw new ArgumentNullException(nameof(configuration), errorMessage);
                }

                RegistryLog.Information(_logger,
                    "Azure Storage connection string not found. Attempting to use Managed Identity for account: {AccountName}.",
                    accountName);
                // Use Managed Identity
                var blobServiceUri = new Uri($"https://{accountName}.blob.core.windows.net");
                clientToUse = new BlobServiceClient(blobServiceUri, new DefaultAzureCredential());
            }
            else
            {
                RegistryLog.Information(_logger, "Using Azure Storage connection string to create BlobServiceClient.");
                clientToUse = new BlobServiceClient(connectionString);
            }
        }

        // Initialize Azure Blob Storage container client
        _containerClient = clientToUse.GetBlobContainerClient(_containerName);

        // Ensure container exists
        try
        {
            RegistryLog.Information(_logger, "Ensuring blob container '{ContainerName}' exists...", _containerName);
            _containerClient.CreateIfNotExists();
            RegistryLog.Information(_logger, "Blob container '{ContainerName}' is ready.", _containerName);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex,
                "Failed to create or verify blob container '{ContainerName}'. This may prevent module operations.",
                _containerName);
            throw; // Re-throw as this is a critical failure for the service's operation.
        }

        LoadExistingModulesAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Lists all modules based on search criteria
    /// </summary>
    public override Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListModulesAsync(request);
    }

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    public override Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        return _databaseService.GetModuleAsync(moduleNamespace, name, provider, version);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public override Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider)
    {
        return _databaseService.GetModuleVersionsAsync(moduleNamespace, name, provider);
    }

    /// <summary>
    ///     Gets the download URL for a specific module version using SAS token
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        // First query the database to get storage metadata
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
        {
            // Module not found in database
            RegistryLog.Warning(_logger, "Module {Namespace}/{Name}/{Provider}/{Version} not found in database",
                moduleNamespace, name, provider, version);
            return null;
        }

        try
        {
            // Get the blob path from storage metadata and generate a client
            var blobPath = moduleStorage.FilePath;
            var blobClient = _containerClient.GetBlobClient(blobPath);

            // Check if the blob exists in Azure Storage
            if (!await blobClient.ExistsAsync())
            {
                // This indicates data inconsistency - database record exists but no blob
                RegistryLog.Warning(_logger,
                    "Module {Namespace}/{Name}/{Provider}/{Version} exists in database but blob not found at {BlobPath}",
                    moduleNamespace, name, provider, version, blobPath);
                return null;
            }

            // Create a SAS token that's valid for the specified time
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = blobPath,
                Resource = "b", // b for blob
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_sasTokenExpiryMinutes)
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            try
            {
                var sasToken = blobClient.GenerateSasUri(sasBuilder);
                return sasToken.ToString();
            }
            catch (InvalidOperationException)
            {
                RegistryLog.Warning(_logger,
                    "Blob client for {Namespace}/{Name}/{Provider}/{Version} cannot generate SAS URIs directly. Falling back to blob URI.",
                    moduleNamespace, name, provider, version);
                return blobClient.Uri.ToString();
            }
        }
        catch (Exception ex)
        {
            // Log any errors during SAS token generation
            RegistryLog.Error(_logger, ex, "Error generating SAS token for module {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return null;
        }
    }

    public override async Task<Stream?> OpenModulePackageStreamAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
            return null;

        var blobClient = _containerClient.GetBlobClient(moduleStorage.FilePath);
        if (!await blobClient.ExistsAsync())
            return null;

        return await blobClient.OpenReadAsync();
    }

    /// <summary>
    ///     Implementation-specific method to upload a module after validation
    /// </summary>
    /// <remarks>
    ///     This method demonstrates the two-step storage process:
    ///     1. Upload the actual module file to Azure Blob Storage
    ///     2. Store the metadata and blob path reference in the PostgreSQL database
    ///     The database stores metadata and a reference to the blob path, while the
    ///     actual module content is stored in Azure Blob Storage.
    /// </remarks>
    protected override async Task<bool> UploadModuleAsyncCore(string moduleNamespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata)
    {
        // Create a consistent blob path format for easy retrieval
        var blobPath = $"{moduleNamespace}/{name}-{provider}-{version}.zip";
        var blobClient = _containerClient.GetBlobClient(blobPath);

        // Check if blob already exists to avoid duplication or allow replacement
        if (await blobClient.ExistsAsync())
        {
            if (!replace)
            {
                RegistryLog.Warning(_logger, "Module {Namespace}/{Name}/{Provider}/{Version} already exists in blob storage",
                    moduleNamespace, name, provider, version);
                return false;
            }

            // Replace requested: delete existing blob
            try
            {
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                RegistryLog.Error(_logger, ex, "Failed to delete existing blob for {Namespace}/{Name}/{Provider}/{Version}",
                    moduleNamespace, name, provider, version);
                return false;
            }
        }

        try
        {
            // Step 1: Upload the actual module content to Azure Blob Storage
            // We store metadata in the blob properties for redundancy and easier recovery
            await blobClient.UploadAsync(moduleContent, new BlobUploadOptions
            {
                Metadata = new Dictionary<string, string>
(StringComparer.Ordinal)
                {
                    { "namespace", moduleNamespace },
                    { "name", name },
                    { "provider", provider },
                    { "version", version },
                    { "description", description },
                    { "publishedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }
                }
            });

            // Step 2: Store module metadata in PostgreSQL database with a reference to the blob
            var module = new ModuleStorage
            {
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = description,
                FilePath = blobPath, // This is the crucial link between database and blob storage
                PublishedAt = DateTime.UtcNow,
                Dependencies = new List<string>(), // Simplified, no dependencies
                Metadata = metadata ?? new ModuleArtifactMetadata()
            };

            if (replace)
            {
                try
                {
                    await _databaseService.RemoveModuleAsync(module);
                }
                catch (Exception ex)
                {
                    RegistryLog.Warning(_logger, ex,
                        "Failed to remove existing database module {Namespace}/{Name}/{Provider}/{Version}; continuing with upsert",
                        moduleNamespace, name, provider, version);
                }
            }

            // Add to database - this stores metadata and the blob path reference
            var result = await _databaseService.AddModuleAsync(module);

            if (!result)
            {
                // Clean up the blob if database insertion fails to maintain consistency
                await blobClient.DeleteAsync();
                RegistryLog.Error(_logger,
                    "Failed to add module {Namespace}/{Name}/{Provider}/{Version} to database, cleaned up blob storage",
                    moduleNamespace, name, provider, version);
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log any errors during upload
            RegistryLog.Error(_logger, ex, "Error uploading module {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);

            // Try to clean up the blob if an error occurred
            try
            {
                if (await blobClient.ExistsAsync()) await blobClient.DeleteAsync();
            }
            catch (Exception cleanupEx)
            {
                RegistryLog.Warning(_logger, cleanupEx,
                    "Failed to clean up blob for module {Namespace}/{Name}/{Provider}/{Version} after upload error",
                    moduleNamespace, name, provider, version);
            }

            return false;
        }
    }

    /// <summary>
    ///     Scans the blob container and loads existing modules into memory
    /// </summary>
    /// <remarks>
    ///     This method demonstrates the recovery capability of our architecture:
    ///     1. If the database is missing metadata but the blobs exist, we can reconstruct the database entries
    ///     2. It ensures consistency between what's in blob storage and what's in the database
    ///     3. It helps with migration scenarios when moving from one database to another
    /// </remarks>
    private async Task LoadExistingModulesAsync()
    {
        try
        {
            RegistryLog.Information(_logger, "Starting synchronization between Azure Blob Storage and PostgreSQL database...");
            var syncCount = 0;

            // List all blobs in the container
            await foreach (var blobItem in _containerClient.GetBlobsAsync())
            {
                try
                {
                    // Get the blob client
                    var blobClient = _containerClient.GetBlobClient(blobItem.Name);

                    // Get blob metadata
                    var properties = await blobClient.GetPropertiesAsync();

                    ModuleStorage? module = null;

                    if (properties.Value.Metadata?.Count > 0)
                    {
                        // Extract module information from metadata (preferred method)
                        var metadata = properties.Value.Metadata;

                        if (metadata.TryGetValue("namespace", out var namespaceName) &&
                            metadata.TryGetValue("name", out var moduleName) &&
                            metadata.TryGetValue("provider", out var provider) &&
                            metadata.TryGetValue("version", out var version) &&
                            metadata.TryGetValue("description", out var description))
                        {
                            // Create module storage object from blob metadata
                            module = new ModuleStorage
                            {
                                Namespace = namespaceName,
                                Name = moduleName,
                                Provider = provider,
                                Version = version,
                                Description = description,
                                FilePath = blobItem.Name, // Store reference to blob location
                                PublishedAt = properties.Value.LastModified.DateTime,
                                Dependencies = new List<string>() // Simplified, no dependencies
                            };
                        }
                    }

                    // Fallback method: try to extract module information from the blob name pattern
                    if (module == null)
                    {
                        var pathParts = blobItem.Name.Split('/');
                        if (pathParts.Length < 2) continue;

                        var namespaceName = pathParts[0];
                        var fileName = Path.GetFileNameWithoutExtension(pathParts[1]);
                        var parts = fileName.Split('-');

                        if (parts.Length < 3) continue;

                        // Last part is version
                        var version = parts[^1];
                        // Second last part is provider
                        var provider = parts[^2];
                        // All remaining parts (if multiple) form the name
                        var name = string.Join("-", parts.Take(parts.Length - 2));

                        // Validate the version string against SemVer 2.0.0 specification
                        if (!SemVerValidator.IsValid(version)) continue;

                        // Create module storage object from blob name
                        module = new ModuleStorage
                        {
                            Namespace = namespaceName,
                            Name = name,
                            Provider = provider,
                            Version = version,
                            Description = $"Module {name} for {provider} (auto-recovered)",
                            FilePath = blobItem.Name, // Store reference to blob location
                            PublishedAt = properties.Value.LastModified.DateTime,
                            Dependencies = new List<string>() // Simplified, no dependencies
                        };
                    }

                    if (module != null)
                    {
                        // Check if this module already exists in the database
                        var existingModule = await _databaseService.GetModuleStorageAsync(
                            module.Namespace, module.Name, module.Provider, module.Version);

                        if (existingModule == null)
                        {
                            // Module exists in blob storage but not in database - synchronize by adding to database
                            var result = await _databaseService.AddModuleAsync(module);
                            if (result)
                            {
                                syncCount++;
                                RegistryLog.Information(_logger,
                                    "Synchronized module {Namespace}/{Name}/{Provider}/{Version} from blob storage to database",
                                    module.Namespace, module.Name, module.Provider, module.Version);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue processing other blobs
                    RegistryLog.Error(_logger, ex, "Error processing blob {BlobName}", blobItem.Name);
                }
            }

            RegistryLog.Information(_logger,
                "Synchronization complete. Added {SyncCount} modules from Azure Blob Storage to the database.",
                syncCount);
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            RegistryLog.Error(_logger, ex, "Error during blob storage/database synchronization");
        }
    }

    public override Task<bool> DeleteModuleVersionAsync(string moduleNamespace, string name, string provider, string version)
    {
        return _databaseService.SoftDeleteModuleAsync(moduleNamespace, name, provider, version);
    }

    public override Task<bool> RestoreModuleVersionAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        return _databaseService.RestoreModuleAsync(moduleNamespace, name, provider, version);
    }

    public override async Task<bool> PurgeModuleVersionAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage =
            await _databaseService.GetModuleStorageIncludingDeletedAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
            return false;

        // Delete from database first (permanent delete)
        var dbResult = await _databaseService.RemoveModuleAsync(moduleStorage);
        if (!dbResult)
            return false;

        // Delete the blob from Azure Storage
        try
        {
            var blobClient = _containerClient.GetBlobClient(moduleStorage.FilePath);
            await blobClient.DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to delete blob for purged module {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            // DB deletion succeeded, blob deletion may have failed - still return true
        }

        return true;
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

    public override async Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        try
        {
            await _containerClient.GetPropertiesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Azure Blob Storage unreachable: {ex.Message}");
        }
    }
}
