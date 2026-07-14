namespace TerraformRegistry.API.Interfaces;

public interface IProviderArtifactStorage
{
    Task<ProviderArtifactSaveResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken);
    Task<ProviderArtifactSaveResult> SaveAsync(
        string relativePath,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken) => SaveAsync(relativePath, content, cancellationToken);
    Task<string> CreateDownloadUrlAsync(string storagePath, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken);
    Task<(bool Healthy, string? Reason)> CheckStorageAsync(CancellationToken cancellationToken);
}

public sealed record ProviderArtifactSaveResult(string StoragePath, long SizeBytes);
