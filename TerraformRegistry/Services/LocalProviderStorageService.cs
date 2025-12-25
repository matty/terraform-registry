using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class LocalProviderStorageService : IProviderStorageService
{
    private readonly string _storagePath;
    private readonly string _baseUrl;
    private readonly ILogger<LocalProviderStorageService> _logger;

    public LocalProviderStorageService(IConfiguration config, ILogger<LocalProviderStorageService> logger)
    {
        _storagePath = config["ModuleStoragePath"] ?? "data/storage";
        _baseUrl = config["BaseUrl"] ?? "http://localhost:5131";
        _logger = logger;

        if (!Directory.Exists(_storagePath))
            Directory.CreateDirectory(_storagePath);

        var providerPath = Path.Combine(_storagePath, "providers");
        if (!Directory.Exists(providerPath))
            Directory.CreateDirectory(providerPath);
    }

    public async Task<string> UploadProviderAsync(string @namespace, string type, string version, string os, string arch, Stream stream)
    {
        var fileName = $"{type}_{version}_{os}_{arch}.zip";
        var relativePath = Path.Combine("providers", @namespace, type, version);
        var fullDir = Path.Combine(_storagePath, relativePath);

        if (!Directory.Exists(fullDir))
            Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(fullDir, fileName);

        using (var fileStream = new FileStream(fullPath, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream);
        }

        // Return a relative path that can be used to construct the download URL later,
        // or used by the file download handler to locate the file.
        // The DB will store this.
        return Path.Combine(relativePath, fileName);
    }

    public async Task<string?> GetProviderDownloadUrlAsync(string @namespace, string type, string version, string os, string arch)
    {
        // Return the API endpoint that serves the file
        return $"{_baseUrl}/v1/providers/{@namespace}/{type}/{version}/download/{os}/{arch}/file";
    }

    public async Task UploadShasumsAsync(string @namespace, string type, string version, Stream stream)
    {
        var relativePath = Path.Combine("providers", @namespace, type, version);
        var fullDir = Path.Combine(_storagePath, relativePath);
        if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(fullDir, "SHA256SUMS");
        using (var fileStream = new FileStream(fullPath, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream);
        }
    }

    public async Task UploadShasumsSigAsync(string @namespace, string type, string version, Stream stream)
    {
        var relativePath = Path.Combine("providers", @namespace, type, version);
        var fullDir = Path.Combine(_storagePath, relativePath);
        if (!Directory.Exists(fullDir)) Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(fullDir, "SHA256SUMS.sig");
        using (var fileStream = new FileStream(fullPath, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream);
        }
    }

    public async Task<string?> GetShasumsDownloadUrlAsync(string @namespace, string type, string version)
    {
        return $"{_baseUrl}/v1/providers/{@namespace}/{type}/{version}/SHA256SUMS";
    }

    public async Task<string?> GetShasumsSigDownloadUrlAsync(string @namespace, string type, string version)
    {
        return $"{_baseUrl}/v1/providers/{@namespace}/{type}/{version}/SHA256SUMS.sig";
    }

    public Task<Stream?> GetFileStreamAsync(string relativePath)
    {
        try
        {
            var fullPath = GetPhysicalFilePath(relativePath);
            if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);

            return Task.FromResult<Stream?>(new FileStream(fullPath, FileMode.Open, FileAccess.Read));
        }
        catch
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    // Helper to get actual file path
    private string GetPhysicalFilePath(string relativePath)
    {
        // Prevent directory traversal
        var fullPath = Path.GetFullPath(Path.Combine(_storagePath, relativePath));
        if (!fullPath.StartsWith(Path.GetFullPath(_storagePath)))
        {
            throw new UnauthorizedAccessException("Access to path denied.");
        }
        return fullPath;
    }
}
