using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
///     Implementation of a module service with local file system storage
/// </summary>
public class LocalModuleService : ModuleService
{
    // Token storage for download links
    private static readonly ConcurrentDictionary<string, (string FilePath, DateTime Expiry)> DownloadTokens = new();
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<LocalModuleService> _logger;
    private readonly string _moduleStoragePath;

    public LocalModuleService(IConfiguration configuration, IDatabaseService databaseService,
        ILogger<LocalModuleService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;

        // Get storage path from configuration, with a reasonable default if not specified
        _moduleStoragePath = configuration["ModuleStoragePath"] ??
                             Path.Combine(Directory.GetCurrentDirectory(), "modules");

        // Log the storage path being used
        _logger.LogInformation("Using local module storage path: {Path}", _moduleStoragePath);

        // Ensure module storage directory exists
        if (!Directory.Exists(_moduleStoragePath)) Directory.CreateDirectory(_moduleStoragePath);

        // Load existing modules from disk
        LoadExistingModules();
    }

    /// <summary>
    ///     Scans the module storage directory and loads existing modules into memory
    /// </summary>
    private void LoadExistingModules()
    {
        try
        {
            // Check if the directory exists
            if (!Directory.Exists(_moduleStoragePath)) return;

            // Scan namespace directories
            foreach (var namespaceDir in Directory.GetDirectories(_moduleStoragePath))
            {
                var namespaceName = Path.GetFileName(namespaceDir);

                // Scan for module zip files
                foreach (var zipFile in Directory.GetFiles(namespaceDir, "*.zip"))
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

            _logger.LogInformation("Loaded modules from disk.");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            _logger.LogError(ex, "Error scanning module directory");
        }
    }

    /// <summary>
    ///     Loads a module from a zip file into memory
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
        // The second last part is provider
        var provider = parts[^2];
        // All remaining parts (if multiple) form the name
        var name = string.Join("-", parts.Take(parts.Length - 2));

        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning(
                "Skipping module {FileName}: Version '{Version}' is not a valid Semantic Version (SemVer 2.0.0)",
                fileName, version);
            return;
        }

        // Try to extract the description from the zip file
        var description = "";
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

                    var metadata = JsonSerializer.Deserialize<ModuleMetadata>(content);

                    if (metadata != null && !string.IsNullOrEmpty(metadata.Description))
                        description = metadata.Description;
                }
            }
        }
        catch
        {
            // If we can't extract the description, use a default
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
            Dependencies = []
        };

        _databaseService.AddModuleAsync(module).Wait();
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
    public override Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        return _databaseService.GetModuleAsync(@namespace, name, provider, version);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public override Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        return _databaseService.GetModuleVersionsAsync(@namespace, name, provider);
    }

    /// <summary>
    ///     Gets the download path for a specific module version
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string @namespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
            return null;

        // Generate a unique token
        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.Add(TokenLifetime);
        DownloadTokens[token] = (moduleStorage.FilePath, expiry);

        // Return the download link (adjust the base path as needed)
        return $"/module/download?token={token}";
    }

    // Helper for endpoint to validate and retrieve the file path
    public static bool TryGetFilePathFromToken(string token, out string filePath)
    {
        filePath = string.Empty;
        if (!DownloadTokens.TryGetValue(token, out var entry)) return false;
        if (entry.Expiry > DateTime.UtcNow)
        {
            filePath = entry.FilePath;
            return true;
        }

        // Expired, remove
        DownloadTokens.TryRemove(token, out _);

        return false;
    }

    /// <summary>
    ///     Implementation-specific method to upload a module after validation
    /// </summary>
    protected override async Task<bool> UploadModuleAsyncImpl(string @namespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace)
    {
        var namespaceDir = Path.Combine(_moduleStoragePath, @namespace);
        if (!Directory.Exists(namespaceDir)) Directory.CreateDirectory(namespaceDir);

        var fileName = $"{name}-{provider}-{version}.zip";
        var tempFileName = $"{fileName}.tmp";
        var tempFilePath = Path.Combine(namespaceDir, tempFileName);
        var finalFilePath = Path.Combine(namespaceDir, fileName);

        try
        {
            await using (var fileStream = File.Create(tempFilePath))
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
                Dependencies = []
            };

            if (replace)
            {
                try
                {
                    await _databaseService.RemoveModuleAsync(module);
                }
                catch
                {
                    // ignore DB remove failures here; Add will handle existence
                }

                try
                {
                    if (File.Exists(finalFilePath)) File.Delete(finalFilePath);
                }
                catch
                {
                    // ignore file delete errors; we'll overwrite if possible
                }
            }

            var dbResult = await _databaseService.AddModuleAsync(module);
            if (dbResult)
                try
                {
                    if (File.Exists(finalFilePath)) File.Delete(finalFilePath);
                    File.Move(tempFilePath, finalFilePath);
                    return true;
                }
                catch (Exception fileMoveEx)
                {
                    try
                    {
                        await _databaseService.RemoveModuleAsync(module);
                    }
                    catch (Exception dbRollbackEx)
                    {
                        _logger.LogError(dbRollbackEx,
                            "Failed to rollback DB entry after file move failure for {Namespace}/{Name}/{Provider}/{Version}",
                            @namespace, name, provider, version);
                    }

                    _logger.LogError(fileMoveEx,
                        "Failed to move file, rolled back DB entry for {Namespace}/{Name}/{Provider}/{Version}",
                        @namespace, name, provider, version);
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                    return false;
                }

            File.Delete(tempFilePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload module {Namespace}/{Name}/{Provider}/{Version}", @namespace, name,
                provider, version);
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            return false;
        }
    }

    public override Task<bool> DeleteModuleVersionAsync(string @namespace, string name, string provider, string version)
    {
        return _databaseService.SoftDeleteModuleAsync(@namespace, name, provider, version);
    }

    public override Task<bool> RestoreModuleVersionAsync(string @namespace, string name, string provider,
        string version)
    {
        return _databaseService.RestoreModuleAsync(@namespace, name, provider, version);
    }

    public override async Task<bool> PurgeModuleVersionAsync(string @namespace, string name, string provider,
        string version)
    {
        var moduleStorage =
            await _databaseService.GetModuleStorageIncludingDeletedAsync(@namespace, name, provider, version);
        if (moduleStorage == null)
            return false;

        // Delete from database first (permanent delete)
        var dbResult = await _databaseService.RemoveModuleAsync(moduleStorage);
        if (!dbResult)
            return false;

        // Delete the file from disk
        try
        {
            if (File.Exists(moduleStorage.FilePath))
                File.Delete(moduleStorage.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file for purged module {Namespace}/{Name}/{Provider}/{Version}",
                @namespace, name, provider, version);
            // DB deletion succeeded, file deletion may have failed - still return true
        }

        return true;
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
        if (!Directory.Exists(_moduleStoragePath))
            return Task.FromResult((false, (string?)"Storage directory does not exist"));
        try
        {
            var testFile = Path.Combine(_moduleStoragePath, ".health-check");
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            return Task.FromResult((true, (string?)null));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, (string?)$"Storage path not writable: {ex.Message}"));
        }
    }
}

