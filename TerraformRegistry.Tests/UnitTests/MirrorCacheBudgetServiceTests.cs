using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorCacheBudgetServiceTests
{
    [Fact]
    public async Task EnsuresCapacityByEvictingReadyProviderPackagesOldestFirst()
    {
        var providerRepository = new Mock<IProviderMirrorRepository>();
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 0))
            .ReturnsAsync(
            [
                ProviderPackage("old", 6, DateTime.UtcNow.AddMinutes(-2)),
                ProviderPackage("new", 4, DateTime.UtcNow.AddMinutes(-1))
            ]);
        providerRepository.Setup(x => x.ListProviderPackagesAsync(null, "ready", 1000, 2))
            .ReturnsAsync([]);
        var moduleRepository = new Mock<IModuleMirrorRepository>();
        moduleRepository.Setup(x => x.ListModulePackagesAsync(null, "ready", 1000, 0)).ReturnsAsync([]);
        var storage = new Mock<IProviderArtifactStorage>();
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var modules = new Mock<IModuleService>();
        var service = new MirrorCacheBudgetService(providerRepository.Object, moduleRepository.Object, storage.Object, modules.Object, new MirrorCacheUsage());

        var result = await service.EnsureCapacityAsync(5, 10, CancellationToken.None);

        Assert.True(result);
        storage.Verify(x => x.DeleteAsync("old", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.DeleteAsync("new", It.IsAny<CancellationToken>()), Times.Never);
        providerRepository.Verify(x => x.UpsertProviderPackageAsync(It.Is<MirrorProviderPackage>(p =>
            p.PackageStoragePath == null && p.CacheSizeBytes == 0 && p.State == "evicted")), Times.Once);
    }

    private static MirrorProviderPackage ProviderPackage(string path, long size, DateTime updatedAt) => new()
    {
        Hostname = "registry.example.com",
        Namespace = "hashicorp",
        Type = "aws",
        Version = "1.0.0",
        Os = "linux",
        Arch = "amd64",
        DownloadUrl = "https://registry.example.com/package",
        PackageStoragePath = path,
        SizeBytes = size,
        CacheSizeBytes = size,
        State = "ready",
        LastSyncAt = updatedAt,
        UpdatedAt = updatedAt
    };
}
