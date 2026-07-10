using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
///     Implementation of a module service with local file system storage
/// </summary>
public class LocalModuleService : ModuleService
{
    // Token storage for download links
    private static readonly ConcurrentDictionary<string, (string FilePath, DateTime Expiry)> DownloadTokens = new(StringComparer.Ordinal);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<LocalModuleService> _logger;
    private readonly string _moduleStoragePath;
    private readonly string _moduleStorageRoot;

    public LocalModuleService(IConfiguration configuration, IDatabaseService databaseService,
        ILogger<LocalModuleService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;

        // Get storage path from configuration, with a reasonable default if not specified
        _moduleStoragePath = configuration["ModuleStoragePath"] ??
                             Path.Combine(Directory.GetCurrentDirectory(), "modules");
        _moduleStorageRoot = Path.GetFullPath(_moduleStoragePath);

        // Log the storage path being used
        RegistryLog.Information(_logger, "Using local module storage path: {Path}", _moduleStorageRoot);

    }

    public override Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_moduleStorageRoot);
        LoadExistingModules();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Scans the module storage directory and loads existing modules into memory
    /// </summary>
    private void LoadExistingModules()
    {
        try
        {
            // Check if the directory exists
            if (!Directory.Exists(_moduleStorageRoot)) return;

            // Scan namespace directories
            foreach (var namespaceDir in Directory.GetDirectories(_moduleStorageRoot))
            {
                var namespaceName = Path.GetFileName(namespaceDir);
                if (!ModuleIdentifierValidator.IsValidSegment(namespaceName))
                {
                    RegistryLog.Warning(_logger, "Skipping module namespace directory with invalid name: {Namespace}",
                        namespaceName);
                    continue;
                }

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
                        RegistryLog.Error(_logger, ex, "Error loading module from {ZipFile}", zipFile);
                    }
                }
            }

            RegistryLog.Information(_logger, "Loaded modules from disk.");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            RegistryLog.Error(_logger, ex, "Error scanning module directory");
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
            RegistryLog.Warning(_logger, "Invalid module filename format: {FileName}", fileName);
            return;
        }

        // Last part is version
        var version = parts[^1];
        // The second last part is provider
        var provider = parts[^2];
        // All remaining parts (if multiple) form the name
        var name = string.Join("-", parts.Take(parts.Length - 2));

        var coordinateError = ModuleIdentifierValidator.GetModuleCoordinateError(namespaceName, name, provider);
        if (coordinateError != null)
        {
            RegistryLog.Warning(_logger, "Skipping module {FileName}: {Message}", fileName, coordinateError);
            return;
        }

        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            RegistryLog.Warning(_logger,
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
    ///     Gets the download path for a specific module version
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null)
            return null;
        if (!IsInsideStorageRoot(moduleStorage.FilePath))
        {
            RegistryLog.Warning(_logger,
                "Refusing to create download token for module outside storage root: {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return null;
        }

        // Generate a unique token
        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.Add(TokenLifetime);
        DownloadTokens[token] = (Path.GetFullPath(moduleStorage.FilePath), expiry);

        // Return the download link (adjust the base path as needed)
        return $"/module/download?token={token}";
    }

    public override async Task<Stream?> OpenModulePackageStreamAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (moduleStorage == null || !File.Exists(moduleStorage.FilePath))
            return null;

        if (!IsInsideStorageRoot(moduleStorage.FilePath))
        {
            RegistryLog.Warning(_logger,
                "Refusing to open module package outside storage root: {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return null;
        }

        return File.OpenRead(Path.GetFullPath(moduleStorage.FilePath));
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
    protected override async Task<bool> UploadModuleAsyncCore(string moduleNamespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata)
    {
        var coordinateError = ModuleIdentifierValidator.GetModuleCoordinateError(moduleNamespace, name, provider);
        if (coordinateError != null)
            throw new ArgumentException(coordinateError);

        var namespaceDir = GetNamespaceDirectory(moduleNamespace);
        if (!Directory.Exists(namespaceDir)) Directory.CreateDirectory(namespaceDir);

        var fileName = $"{name}-{provider}-{version}.zip";
        var tempFileName = $"{fileName}.tmp";
        var tempFilePath = GetContainedPath(namespaceDir, tempFileName);
        var finalFilePath = GetContainedPath(namespaceDir, fileName);

        try
        {
            await using (var fileStream = File.Create(tempFilePath))
            {
                await moduleContent.CopyToAsync(fileStream);
            }

            var module = new ModuleStorage
            {
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = description,
                FilePath = finalFilePath,
                PublishedAt = DateTime.UtcNow,
                Dependencies = [],
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
                        "Failed to remove existing database module {Namespace}/{Name}/{Provider}/{Version}; continuing with add",
                        moduleNamespace, name, provider, version);
                }

                try
                {
                    if (File.Exists(finalFilePath)) File.Delete(finalFilePath);
                }
                catch (Exception ex)
                {
                    RegistryLog.Warning(_logger, ex,
                        "Failed to delete existing module file {Path}; continuing with overwrite", finalFilePath);
                }
            }

            var dbResult = await _databaseService.AddModuleAsync(module);
            if (dbResult)
            {
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
                        RegistryLog.Error(_logger, dbRollbackEx,
                            "Failed to rollback DB entry after file move failure for {Namespace}/{Name}/{Provider}/{Version}",
                            moduleNamespace, name, provider, version);
                    }

                    RegistryLog.Error(_logger, fileMoveEx,
                        "Failed to move file, rolled back DB entry for {Namespace}/{Name}/{Provider}/{Version}",
                        moduleNamespace, name, provider, version);
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                    return false;
                }
            }

            File.Delete(tempFilePath);
            return false;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to upload module {Namespace}/{Name}/{Provider}/{Version}", moduleNamespace, name,
                provider, version);
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            return false;
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
        if (!IsInsideStorageRoot(moduleStorage.FilePath))
        {
            RegistryLog.Warning(_logger,
                "Refusing to purge module with file path outside storage root: {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return false;
        }

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
            RegistryLog.Error(_logger, ex, "Failed to delete file for purged module {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            // DB deletion succeeded, file deletion may have failed - still return true
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
        if (!Directory.Exists(_moduleStorageRoot))
            return (false, "Storage directory does not exist");
        try
        {
            var testFile = Path.Combine(_moduleStorageRoot, $".health-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(testFile, "ok");
            File.Delete(testFile);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Storage path not writable: {ex.Message}");
        }
    }

    private string GetNamespaceDirectory(string moduleNamespace)
    {
        var path = Path.GetFullPath(Path.Combine(_moduleStorageRoot, moduleNamespace));
        if (!IsInsideStorageRoot(path))
            throw new ArgumentException("Module namespace resolves outside the storage root.", nameof(moduleNamespace));

        return path;
    }

    private string GetContainedPath(string directory, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!IsInsideStorageRoot(path))
            throw new ArgumentException("Module file path resolves outside the storage root.", nameof(fileName));

        return path;
    }

    private bool IsInsideStorageRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(_moduleStorageRoot, fullPath);

        return relativePath != "." &&
               !relativePath.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }
}
