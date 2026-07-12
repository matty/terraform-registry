using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class LocalProviderArtifactStorageTests
{
    [Fact]
    public async Task SaveAsyncStoresArtifactInsideProviderStorageRoot()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.FromMinutes(10));

        await using var content = new MemoryStream([1, 2, 3]);

        var result = await storage.SaveAsync("acme/example/1.0.0/linux_amd64.zip", content, CancellationToken.None);

        Assert.Equal("acme/example/1.0.0/linux_amd64.zip", result.StoragePath);
        Assert.Equal(3, result.SizeBytes);
        Assert.True(File.Exists(Path.Combine(temp.Path, result.StoragePath)));
    }

    [Fact]
    public async Task OpenReadAsyncReturnsStoredArtifactContent()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.FromMinutes(10));
        await using var content = new MemoryStream([4, 5, 6]);
        var result = await storage.SaveAsync("acme/example/1.0.0/file.zip", content, CancellationToken.None);

        await using var loaded = await storage.OpenReadAsync(result.StoragePath, CancellationToken.None);

        Assert.NotNull(loaded);
        await using var copy = new MemoryStream();
        await loaded!.CopyToAsync(copy);
        Assert.Equal([4, 5, 6], copy.ToArray());
    }

    [Fact]
    public async Task CreateDownloadUrlAsyncReturnsTokenForStoredArtifact()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.FromMinutes(10));
        await using var content = new MemoryStream([1]);
        var result = await storage.SaveAsync("acme/example/1.0.0/file.zip", content, CancellationToken.None);

        var tokenUrl = await storage.CreateDownloadUrlAsync(result.StoragePath, CancellationToken.None);
        var token = tokenUrl.Split("token=", StringSplitOptions.None)[1];

        Assert.StartsWith("/provider/download?token=", tokenUrl, StringComparison.Ordinal);
        Assert.True(storage.TryGetFilePathFromToken(token, out var filePath));
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, result.StoragePath)), filePath);
    }

    [Fact]
    public async Task OpenTokenAsyncReturnsNullAfterTokenExpires()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.Zero);
        await using var content = new MemoryStream([1]);
        var result = await storage.SaveAsync("acme/example/1.0.0/file.zip", content, CancellationToken.None);

        var tokenUrl = await storage.CreateDownloadUrlAsync(result.StoragePath, CancellationToken.None);
        var token = tokenUrl.Split("token=", StringSplitOptions.None)[1];
        Assert.False(storage.TryGetFilePathFromToken(token, out _));
    }

    [Fact]
    public async Task SaveAsyncRejectsPathsOutsideStorageRoot()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.FromMinutes(10));
        await using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync("../outside.zip", content, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsyncRemovesStoredArtifact()
    {
        using var temp = new TempDirectory();
        var storage = CreateStorage(temp.Path, TimeSpan.FromMinutes(10));
        await using var content = new MemoryStream([1]);
        var result = await storage.SaveAsync("acme/example/1.0.0/file.zip", content, CancellationToken.None);

        Assert.True(await storage.DeleteAsync(result.StoragePath, CancellationToken.None));

        Assert.False(await storage.ExistsAsync(result.StoragePath, CancellationToken.None));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"provider-storage-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }

    private static LocalProviderArtifactStorage CreateStorage(string path, TimeSpan lifetime)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactDownloadTokens:SigningKey"] = "test-signing-key-that-is-long-enough-to-be-safe-0123456789"
            })
            .Build();
        return new LocalProviderArtifactStorage(
            path,
            lifetime,
            NullLogger<LocalProviderArtifactStorage>.Instance,
            new ArtifactDownloadTokenService(configuration));
    }
}
