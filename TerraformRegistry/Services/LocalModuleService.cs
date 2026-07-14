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
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<LocalModuleService> _logger;
    private readonly string _moduleStoragePath;
    private readonly string _moduleStorageRoot;
    private readonly ArtifactDownloadTokenService _tokens;

    public LocalModuleService(IConfiguration configuration, IDatabaseService databaseService,
        ILogger<LocalModuleService> logger, ArtifactDownloadTokenService? tokens = null)
    {
        _databaseService = databaseService;
        _logger = logger;
        _tokens = tokens ?? new ArtifactDownloadTokenService(configuration);

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
        return Task.CompletedTask;
    }

    public override Task ReconcileStorageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
    public override Task<ModuleList> ListModulesAsync(ModuleSearchRequest request, CancellationToken cancellationToken = default)
    {
        return _databaseService.ListModulesAsync(request, cancellationToken);
    }

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    public override Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version,
        CancellationToken cancellationToken = default)
    {
        return _databaseService.GetModuleAsync(moduleNamespace, name, provider, version, cancellationToken);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public override Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider,
        CancellationToken cancellationToken = default)
    {
        return _databaseService.GetModuleVersionsAsync(moduleNamespace, name, provider, cancellationToken);
    }

    /// <summary>
    ///     Gets the download path for a specific module version
    /// </summary>
    public override async Task<string?> GetModuleDownloadPathAsync(string moduleNamespace, string name, string provider,
        string version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var moduleStorage = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version, cancellationToken);
        if (moduleStorage == null)
            return null;
        if (!IsInsideStorageRoot(moduleStorage.FilePath))
        {
            RegistryLog.Warning(_logger,
                "Refusing to create download token for module outside storage root: {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return null;
        }

        var filePath = Path.GetFullPath(moduleStorage.FilePath);
        if (!File.Exists(filePath))
        {
            RegistryLog.Warning(_logger,
                "Module {Namespace}/{Name}/{Provider}/{Version} exists in the database but its package is missing from local storage.",
                moduleNamespace, name, provider, version);
            return null;
        }

        // Generate a unique token
        var token = _tokens.Create("module", Path.GetRelativePath(_moduleStorageRoot, filePath), TokenLifetime);

        var archiveHint = ModuleArchiveFormat.GetGoGetterHint(moduleStorage);
        return string.IsNullOrEmpty(archiveHint)
            ? $"/module/download?token={token}"
            : $"/module/download?token={token}&archive={Uri.EscapeDataString(archiveHint)}";
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
    public bool TryGetFilePathFromToken(string token, out string filePath)
    {
        filePath = string.Empty;
        if (!_tokens.TryValidate(token, "module", out var path)) return false;
        if (Path.IsPathRooted(path)) return false;
        var candidate = Path.GetFullPath(Path.Join(_moduleStorageRoot, path));
        if (!IsInsideStorageRoot(candidate)) return false;
        filePath = candidate;
        return true;
    }

    /// <summary>
    ///     Implementation-specific method to upload a module after validation
    /// </summary>
    protected override async Task<bool> UploadModuleAsyncCore(string moduleNamespace, string name, string provider,
        string version, Stream moduleContent, string description, bool replace, ModuleArtifactMetadata? metadata,
        CancellationToken cancellationToken)
    {
        var coordinateError = ModuleIdentifierValidator.GetModuleCoordinateError(moduleNamespace, name, provider);
        if (coordinateError != null)
            throw new ArgumentException(coordinateError);

        var existing = await _databaseService.GetModuleStorageAsync(moduleNamespace, name, provider, version);
        if (!replace && existing is not null)
            return false;

        var now = DateTime.UtcNow;
        var attemptId = Guid.NewGuid();
        var namespaceDir = GetNamespaceDirectory(moduleNamespace);
        var stagingDirectory = GetContainedPath(_moduleStorageRoot, ".staging");
        var publicationDirectory = GetContainedPath(namespaceDir, ".published");
        var fileName = $"{name}-{provider}-{version}{ModuleArchiveFormat.GetFileSuffix(metadata)}";
        var stagingFilePath = GetContainedPath(stagingDirectory, $"{attemptId:N}-{fileName}");
        var finalDirectory = GetContainedPath(publicationDirectory, attemptId.ToString("N"));
        var finalFilePath = GetContainedPath(finalDirectory, fileName);
        var attempt = new ModulePublicationAttempt
        {
            Id = attemptId,
            Namespace = moduleNamespace,
            Name = name,
            Provider = provider,
            Version = version,
            State = ModulePublicationAttemptState.Staged,
            StagingKey = Path.GetRelativePath(_moduleStorageRoot, stagingFilePath).Replace(Path.DirectorySeparatorChar, '/'),
            CreatedAt = now,
            UpdatedAt = now
        };
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = attemptId,
            Namespace = moduleNamespace,
            Name = name,
            Provider = provider,
            Version = version,
            State = ModuleExtractionJobState.Staged,
            CreatedAt = now,
            UpdatedAt = now
        };
        var committed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _databaseService.CreatePublicationAttemptWithExtractionJobAsync(attempt, job, cancellationToken);
            Directory.CreateDirectory(stagingDirectory);
            await using (var fileStream = File.Create(stagingFilePath))
            {
                await moduleContent.CopyToAsync(fileStream, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(finalDirectory);
            File.Move(stagingFilePath, finalFilePath);

            var module = new ModuleStorage
            {
                Namespace = moduleNamespace,
                Name = name,
                Provider = provider,
                Version = version,
                Description = description,
                FilePath = finalFilePath,
                PublishedAt = now,
                Dependencies = [],
                Metadata = metadata ?? new ModuleArtifactMetadata()
            };

            cancellationToken.ThrowIfCancellationRequested();
            committed = await _databaseService.TryCommitStagedPublicationAsync(attempt, module, existing, cancellationToken);
            if (committed)
                return true;

            CleanupOwnedArtifact(finalDirectory, stagingFilePath);
            await _databaseService.TryFailStagedPublicationAsync(attemptId, "Catalog changed before publication could commit.", CancellationToken.None);
            return false;
        }
        catch (OperationCanceledException)
        {
            if (!committed)
            {
                CleanupOwnedArtifact(finalDirectory, stagingFilePath);
                await TryFailPublicationAttemptAsync(attemptId, "Publication canceled.");
            }
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(_logger, ex, "Failed to publish module {Namespace}/{Name}/{Provider}/{Version}", moduleNamespace,
                name, provider, version);
            if (!committed)
            {
                CleanupOwnedArtifact(finalDirectory, stagingFilePath);
                await TryFailPublicationAttemptAsync(attemptId, ex.Message);
            }

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

        try
        {
            if (File.Exists(moduleStorage.FilePath))
                File.Delete(moduleStorage.FilePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(_logger, ex, "Failed to delete file for purged module {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
            return false;
        }

        return await _databaseService.RemoveModuleAsync(moduleStorage);
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

    private void CleanupOwnedArtifact(string finalDirectory, string stagingFilePath)
    {
        try
        {
            if (File.Exists(stagingFilePath))
                File.Delete(stagingFilePath);
            if (Directory.Exists(finalDirectory))
                Directory.Delete(finalDirectory, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Warning(_logger, ex, "Failed to clean up the owned local publication artifact.");
        }
    }

    private async Task TryFailPublicationAttemptAsync(Guid attemptId, string reason)
    {
        try
        {
            await _databaseService.TryFailStagedPublicationAsync(attemptId, reason, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistryLog.Error(_logger, ex, "Failed to mark local publication attempt {AttemptId} as failed.", attemptId);
        }
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
