namespace TerraformRegistry.Services;

using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>
/// Implementation of module service with local file system storage
/// </summary>
public class LocalModuleService : ModuleService
{
    private readonly IDatabaseService _databaseService;
    private readonly string _moduleStoragePath;
    private readonly ILogger<LocalModuleService> _logger;

    // Token storage for download links
    private static readonly ConcurrentDictionary<string, (string FilePath, DateTime Expiry)> _downloadTokens = new();
    private static readonly TimeSpan _tokenLifetime = TimeSpan.FromMinutes(10);

    public LocalModuleService(IConfiguration configuration, IDatabaseService databaseService, ILogger<LocalModuleService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;

        // Get storage path from configuration, with a reasonable default if not specified
        _moduleStoragePath = configuration["ModuleStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "modules");

        // Log the storage path being used
        _logger.LogInformation("Using local module storage path: {Path}", _moduleStoragePath);

        // Ensure module storage directory exists
        if (!Directory.Exists(_moduleStoragePath))
        {
            Directory.CreateDirectory(_moduleStoragePath);
        }

        // Load existing modules from disk
        LoadExistingModules();
    }

    /// <summary>
    /// Scans the module storage directory and loads existing modules into memory
    /// </summary>
    private void LoadExistingModules()
    {
        try
        {
            // Check if the directory exists
            if (!Directory.Exists(_moduleStoragePath))
            {
                return;
            }

            // Scan namespace directories
            foreach (var namespaceDir in Directory.GetDirectories(_moduleStoragePath))
            {
                var namespaceName = Path.GetFileName(namespaceDir);

                // Scan for module zip files
                foreach (var zipFile in Directory.GetFiles(namespaceDir, "*.zip"))
                {
                    try
                    {
                        LoadModuleFromZip(zipFile, namespaceName);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue processing other files
                        _logger.LogError(ex, "Error loading module from {ZipFile}", zipFile);
                    }
                }
            }

            _logger.LogInformation("Loaded modules from disk.");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            _logger.LogError(ex, "Error scanning module directory");
        }
    }

    /// <summary>
    /// Loads a module from a zip file into memory
    /// </summary>
    private void LoadModuleFromZip(string zipFilePath, string namespaceName)
    {
        // Extract module information from filename
        // Expected format: name-provider-version.zip
        var fileName = Path.GetFileNameWithoutExtension(zipFilePath);
        var parts = fileName.Split('-');

        if (parts.Length < 3)
        {
            _logger.LogWarning("Invalid module filename format: {FileName}", fileName);
            return;
        }

        // Last part is version
        var version = parts[^1];
        // Second last part is provider
        var provider = parts[^2];
        // All remaining parts (if multiple) form the name
        var name = string.Join("-", parts.Take(parts.Length - 2));

        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Skipping module {FileName}: Version '{Version}' is not a valid Semantic Version (SemVer 2.0.0)", fileName, version);
            return;
        }

        // Try to extract description from the zip file
        string description = "";
        try
        {
            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                // Look for module metadata in various common files
                var metadataFile = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("module.json", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));

                if (metadataFile != null)
                {
                    using var stream = metadataFile.Open();
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    var metadata = JsonSerializer.Deserialize(content, AppJsonSerializerContext.Default.ModuleMetadata);

                    if (metadata != null && !string.IsNullOrEmpty(metadata.Description))
                    {
                        description = metadata.Description;
                    }
                }
            }
        }
        catch
        {
            // If we can't extract description, use a default
            description = $"Module {name} for {provider}";
        }

        // Create module storage object
        var module = new ModuleStorage
        {
            Namespace = namespaceName,
            Name = name,
            Provider = provider,
            Version = version,
            Description = description,
            FilePath = zipFilePath,
            PublishedAt = File.GetCreationTimeUtc(zipFilePath),
            Dependencies = new List<string>() // Simplified, no dependencies
        };

        // Add to database
        _databaseService.AddModuleAsync(module).Wait();
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
    /// Gets the download path for a specific module version
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider, string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
            return null;

        // Generate a unique token
        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.Add(_tokenLifetime);
        _downloadTokens[token] = (moduleStorage.FilePath, expiry);

        // Return the download link (adjust base path as needed)
        return $"/module/download?token={token}";
    }

    // Helper for endpoint to validate and retrieve file path
    public static bool TryGetFilePathFromToken(string token, out string filePath)
    {
        filePath = string.Empty;
        if (_downloadTokens.TryGetValue(token, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
            {
                filePath = entry.FilePath;
                return true;
            }
            // Expired, remove
            _downloadTokens.TryRemove(token, out _);
        }
        return false;
    }

    /// <summary>
    /// Implementation-specific method to upload a module after validation
    /// </summary>
    protected override async Task<bool> UploadModuleAsyncImpl(string @namespace, string name, string provider, string version, Stream moduleContent, string description)
    {
        // Create namespace directory
        var namespaceDir = Path.Combine(_moduleStoragePath, @namespace);
        if (!Directory.Exists(namespaceDir))
        {
            Directory.CreateDirectory(namespaceDir);
        }

        // Save the module zip file as a temporary file first
        var fileName = $"{name}-{provider}-{version}.zip";
        var tempFileName = $"{fileName}.tmp";
        var tempFilePath = Path.Combine(namespaceDir, tempFileName);
        var finalFilePath = Path.Combine(namespaceDir, fileName);

        try
        {
            using (var fileStream = File.Create(tempFilePath))
            {
                await moduleContent.CopyToAsync(fileStream);
            }

            var module = new ModuleStorage
            {
                Namespace = @namespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = description,
                FilePath = finalFilePath,
                PublishedAt = DateTime.UtcNow,
                Dependencies = new List<string>()
            };

            var dbResult = await _databaseService.AddModuleAsync(module);
            if (dbResult)
            {
                try
                {
                    // Move temp file to final file name
                    if (File.Exists(finalFilePath))
                    {
                        File.Delete(finalFilePath);
                    }
                    File.Move(tempFilePath, finalFilePath);
                    return true;
                }
                catch (Exception fileMoveEx)
                {
                    // Rollback DB entry if file move fails
                    try
                    {
                        await _databaseService.RemoveModuleAsync(module);
                    }
                    catch (Exception dbRollbackEx)
                    {
                        _logger.LogError(dbRollbackEx, "Failed to rollback DB entry after file move failure for {Namespace}/{Name}/{Provider}/{Version}", @namespace, name, provider, version);
                    }
                    _logger.LogError(fileMoveEx, "Failed to move file, rolled back DB entry for {Namespace}/{Name}/{Provider}/{Version}", @namespace, name, provider, version);
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                    return false;
                }
            }
            else
            {
                // DB failed, delete temp file
                File.Delete(tempFilePath);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload module {Namespace}/{Name}/{Provider}/{Version}", @namespace, name, provider, version);
            // Clean up temp file if it exists
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            return false;
        }
    }
}