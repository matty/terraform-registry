namespace TerraformRegistry.Services;

using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Implementation of module service with local file system storage
/// </summary>
public class LocalModuleService : ModuleService
{
    private readonly IDatabaseService _databaseService;
    private readonly string _moduleStoragePath;

    public LocalModuleService(IConfiguration configuration, IDatabaseService databaseService)
    {
        _databaseService = databaseService;

        // Get storage path from configuration, with a reasonable default if not specified
        _moduleStoragePath = configuration["ModuleStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "modules");

        // Log the storage path being used
        Console.WriteLine($"Using local module storage path: {_moduleStoragePath}");

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
                        Console.WriteLine($"Error loading module from {zipFile}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"Loaded modules from disk.");
        }
        catch (Exception ex)
        {
            // Log any errors during initialization
            Console.WriteLine($"Error scanning module directory: {ex.Message}");
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
            Console.WriteLine($"Invalid module filename format: {fileName}");
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
            Console.WriteLine($"Skipping module {fileName}: Version '{version}' is not a valid Semantic Version (SemVer 2.0.0)");
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
        return moduleStorage?.FilePath;
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

        // Save the module zip file
        var fileName = $"{name}-{provider}-{version}.zip";
        var filePath = Path.Combine(namespaceDir, fileName);

        using (var fileStream = File.Create(filePath))
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
            FilePath = filePath,
            PublishedAt = DateTime.UtcNow, // Use current time for newly uploaded modules
            Dependencies = new List<string>() // Simplified, no dependencies
        };

        return await _databaseService.AddModuleAsync(module);
    }
}