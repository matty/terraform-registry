namespace TerraformRegistry.AzureBlob;

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

/// <summary>
/// Implementation of module service using Azure Blob Storage
/// </summary>
public class AzureBlobModuleService : ModuleService
{
    private readonly IDatabaseService _databaseService;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly string _containerName;
    private readonly int _sasTokenExpiryMinutes;
    private readonly ILogger<AzureBlobModuleService> _logger;

    public AzureBlobModuleService(IConfiguration configuration, IDatabaseService databaseService, ILogger<AzureBlobModuleService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;

        // Get Azure Storage connection settings from configuration
        var connectionString = configuration["AzureStorage:ConnectionString"]
            ?? throw new ArgumentNullException("AzureStorage:ConnectionString", "Azure Storage connection string is required");

        _containerName = configuration["AzureStorage:ContainerName"] ?? "modules";
        _sasTokenExpiryMinutes = int.Parse(configuration["AzureStorage:SasTokenExpiryMinutes"] ?? "5");

        // Initialize Azure Blob Storage clients
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

        // Ensure container exists
        _containerClient.CreateIfNotExists(PublicAccessType.None);

        // Load existing modules from Azure Blob Storage
        LoadExistingModules();
    }

    /// <summary>
    /// Lists all modules based on search criteria
    /// </summary>
    public override Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        return _databaseService.ListModulesAsync(request);
    }

    /// <summary>
    /// Gets detailed information about a specific module
    /// </summary>
    public override Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        return _databaseService.GetModuleAsync(@namespace, name, provider, version);
    }

    /// <summary>
    /// Gets all versions of a specific module
    /// </summary>
    public override Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        return _databaseService.GetModuleVersionsAsync(@namespace, name, provider);
    }

    /// <summary>
    /// Gets the download URL for a specific module version using SAS token
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider, string version)
    {
        // First query the database to get storage metadata
        var moduleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
        {
            // Module not found in database
            _logger.LogWarning($"Module {@namespace}/{name}/{provider}/{version} not found in database");
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
                _logger.LogWarning($"Module {@namespace}/{name}/{provider}/{version} exists in database but blob not found at {blobPath}");
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

            // Generate the SAS token URI that includes the full URL
            var sasToken = blobClient.GenerateSasUri(sasBuilder);

            return sasToken.ToString();
        }
        catch (Exception ex)
        {
            // Log any errors during SAS token generation
            _logger.LogError(ex, $"Error generating SAS token for module {@namespace}/{name}/{provider}/{version}");
            return null;
        }
    }

    /// <summary>
    /// Implementation-specific method to upload a module after validation
    /// </summary>
    /// <remarks>
    /// This method demonstrates the two-step storage process:
    /// 1. Upload the actual module file to Azure Blob Storage
    /// 2. Store the metadata and blob path reference in the PostgreSQL database
    /// 
    /// The database stores metadata and a reference to the blob path, while the
    /// actual module content is stored in Azure Blob Storage.
    /// </remarks>
    protected override async Task<bool> UploadModuleAsyncImpl(string @namespace, string name, string provider, string version, Stream moduleContent, string description)
    {
        // Create a consistent blob path format for easy retrieval
        var blobPath = $"{@namespace}/{name}-{provider}-{version}.zip";
        var blobClient = _containerClient.GetBlobClient(blobPath);

        // Check if blob already exists to avoid duplication
        if (await blobClient.ExistsAsync())
        {
            _logger.LogWarning($"Module {@namespace}/{name}/{provider}/{version} already exists in blob storage");
            return false;
        }

        try
        {
            // Step 1: Upload the actual module content to Azure Blob Storage
            // We store metadata in the blob properties for redundancy and easier recovery
            await blobClient.UploadAsync(moduleContent, new BlobUploadOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "namespace", @namespace },
                    { "name", name },
                    { "provider", provider },
                    { "version", version },
                    { "description", description },
                    { "publishedAt", DateTime.UtcNow.ToString("o") }
                }
            });

            // Step 2: Store module metadata in PostgreSQL database with a reference to the blob
            var module = new ModuleStorage
            {
                Namespace = @namespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = description,
                FilePath = blobPath, // This is the crucial link between database and blob storage
                PublishedAt = DateTime.UtcNow,
                Dependencies = new List<string>() // Simplified, no dependencies
            };

            // Add to database - this stores metadata and the blob path reference
            var result = await _databaseService.AddModuleAsync(module);

            if (!result)
            {
                // Clean up the blob if database insertion fails to maintain consistency
                await blobClient.DeleteAsync();
                _logger.LogError($"Failed to add module {@namespace}/{name}/{provider}/{version} to database, cleaned up blob storage");
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log any errors during upload
            _logger.LogError(ex, $"Error uploading module {@namespace}/{name}/{provider}/{version}");

            // Try to clean up the blob if an error occurred
            try
            {
                if (await blobClient.ExistsAsync())
                {
                    await blobClient.DeleteAsync();
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            return false;
        }
    }

    /// <summary>
    /// Scans the blob container and loads existing modules into memory
    /// </summary>
    /// <remarks>
    /// This method demonstrates the recovery capability of our architecture:
    /// 1. If the database is missing metadata but the blobs exist, we can reconstruct the database entries
    /// 2. It ensures consistency between what's in blob storage and what's in the database
    /// 3. It helps with migration scenarios when moving from one database to another
    /// </remarks>
    private async void LoadExistingModules()
    {
        try
        {
            _logger.LogInformation("Starting synchronization between Azure Blob Storage and PostgreSQL database...");
            int syncCount = 0;

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

                    if (properties.Value.Metadata.Count > 0)
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
                                FilePath = blobItem.Name,  // Store reference to blob location
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
                            FilePath = blobItem.Name,  // Store reference to blob location
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
                                _logger.LogInformation($"Synchronized module {module.Namespace}/{module.Name}/{module.Provider}/{module.Version} from blob storage to database");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue processing other blobs
                    _logger.LogError(ex, $"Error processing blob {blobItem.Name}");
                }
            }

            _logger.LogInformation($"Synchronization complete. Added {syncCount} modules from Azure Blob Storage to the database.");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            _logger.LogError(ex, "Error during blob storage/database synchronization");
        }
    }
}