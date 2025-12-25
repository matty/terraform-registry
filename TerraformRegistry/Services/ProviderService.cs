using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class ProviderService : IProviderService
{
    private readonly IDatabaseService _db;
    private readonly ILogger<ProviderService> _logger;
    private readonly IConfiguration _config;
    private readonly IProviderStorageService _storage;

    public ProviderService(IDatabaseService db, ILogger<ProviderService> logger, IConfiguration config, IProviderStorageService storage)
    {
        _db = db;
        _logger = logger;
        _config = config;
        _storage = storage;
    }

    public async Task<ProviderVersions?> GetProviderVersionsAsync(string @namespace, string type)
    {
        return await _db.GetProviderVersionsAsync(@namespace, type);
    }

    public async Task<ProviderPackage?> GetProviderPackageAsync(string @namespace, string type, string version, string os, string arch)
    {
        var package = await _db.GetProviderPackageAsync(@namespace, type, version, os, arch);
        if (package == null) return null;

        // Dynamic URL generation for Storage Providers (like Azure SAS)
        // We override the DB stored URL (which might be a path or stale) with a fresh one from storage service.
        var dynamicUrl = await _storage.GetProviderDownloadUrlAsync(@namespace, type, version, os, arch);
        if (!string.IsNullOrEmpty(dynamicUrl))
        {
            package.DownloadUrl = dynamicUrl;
        }

        return package;
    }

    public async Task UploadShasumsAsync(string @namespace, string type, string version, Stream stream)
    {
        await _storage.UploadShasumsAsync(@namespace, type, version, stream);
    }

    public async Task UploadShasumsSigAsync(string @namespace, string type, string version, Stream stream)
    {
        await _storage.UploadShasumsSigAsync(@namespace, type, version, stream);
    }

    public async Task<ProviderPackage> UploadProviderAsync(string @namespace, string type, string version, string os, string arch, string filename, Stream stream, string shasum, string signingKeyId, List<string>? protocols = null)
    {
        // Validate Key Exists
        var key = await _db.GetGpgKeyAsync(@namespace, signingKeyId);
        if (key == null)
        {
            throw new ArgumentException($"GPG Key {signingKeyId} not found for namespace {@namespace}");
        }

        // 1. Upload file to storage
        var storagePath = await _storage.UploadProviderAsync(@namespace, type, version, os, arch, stream);

        // 2. Add to DB
        var providerPackage = new ProviderPackage
        {
            Os = os,
            Arch = arch,
            Filename = filename,
            DownloadUrl = storagePath,
            Shasum = shasum,
            Protocols = protocols ?? new List<string> { "5.0" },
        };

        await _db.AddProviderPackageAsync(@namespace, type, version, os, arch, filename, storagePath, shasum, JsonSerializer.Serialize(providerPackage.Protocols), signingKeyId);

        return providerPackage;
    }

    public async Task<IEnumerable<GpgKey>> GetGpgKeysAsync(string @namespace)
    {
        return await _db.GetGpgKeysAsync(@namespace);
    }

    public async Task<GpgKey?> GetGpgKeyAsync(string @namespace, string keyId)
    {
        return await _db.GetGpgKeyAsync(@namespace, keyId);
    }

    public async Task AddGpgKeyAsync(GpgKey key)
    {
        // Add validation here if needed
        await _db.AddGpgKeyAsync(key);
    }
}
