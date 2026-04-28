using System.Collections.Concurrent;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public sealed class LocalProviderArtifactStorage : IProviderArtifactStorage
{
    private static readonly ConcurrentDictionary<string, (string FilePath, DateTime Expiry)> DownloadTokens =
        new(StringComparer.Ordinal);
    private readonly ILogger<LocalProviderArtifactStorage> _logger;
    private readonly string _storageRoot;
    private readonly TimeSpan _tokenLifetime;

    public LocalProviderArtifactStorage(string storageRoot, TimeSpan tokenLifetime, ILogger<LocalProviderArtifactStorage> logger)
    {
        _storageRoot = Path.GetFullPath(storageRoot);
        _tokenLifetime = tokenLifetime;
        _logger = logger;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<ProviderArtifactSaveResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        var targetPath = GetContainedPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var file = File.Create(targetPath);
        await content.CopyToAsync(file, cancellationToken);

        return new ProviderArtifactSaveResult(GetStoragePath(targetPath), file.Length);
    }

    public Task<string> CreateDownloadUrlAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = GetContainedPath(storagePath);
        var token = Guid.NewGuid().ToString("N");
        DownloadTokens[token] = (fullPath, DateTime.UtcNow.Add(_tokenLifetime));
        return Task.FromResult($"/provider/download?token={token}");
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = GetContainedPath(storagePath);
        return Task.FromResult<Stream?>(File.Exists(fullPath) ? File.OpenRead(fullPath) : null);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken)
    {
        return Task.FromResult(File.Exists(GetContainedPath(storagePath)));
    }

    public Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        var fullPath = GetContainedPath(storagePath);
        if (!File.Exists(fullPath)) return Task.FromResult(false);
        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<(bool Healthy, string? Reason)> CheckStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_storageRoot);
            var probe = Path.Combine(_storageRoot, $".health-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult((true, (string?)null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider artifact local storage health check failed");
            return Task.FromResult((false, (string?)ex.Message));
        }
    }

    public static bool TryGetFilePathFromToken(string token, out string filePath)
    {
        filePath = string.Empty;
        if (!DownloadTokens.TryGetValue(token, out var entry)) return false;
        if (entry.Expiry <= DateTime.UtcNow)
        {
            DownloadTokens.TryRemove(token, out _);
            return false;
        }

        filePath = entry.FilePath;
        return true;
    }

    private string GetContainedPath(string storagePath)
    {
        if (Path.IsPathRooted(storagePath))
            throw new InvalidOperationException("Provider artifact path escapes storage root.");

        var candidate = Path.GetFullPath(Path.Combine(_storageRoot, storagePath));
        var relative = Path.GetRelativePath(_storageRoot, candidate);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Provider artifact path escapes storage root.");

        return candidate;
    }

    private string GetStoragePath(string fullPath)
    {
        return Path.GetRelativePath(_storageRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
