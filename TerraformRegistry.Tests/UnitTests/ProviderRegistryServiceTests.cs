using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class ProviderRegistryServiceTests
{
    [Fact]
    public async Task GetVersionsAsync_ReturnsNullWhenRepositoryReturnsNoVersions()
    {
        var repository = new Mock<IProviderRepository>();
        var service = CreateService(repository);
        repository.Setup(x => x.GetProviderVersionsAsync("acme", "example"))
            .ReturnsAsync([]);

        var result = await service.GetVersionsAsync("acme", "example");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPackageAsync_ReturnsPackageResponseWithSignedAssetUrlsAndRecordsDownload()
    {
        var repository = new Mock<IProviderRepository>();
        var storage = new Mock<IProviderArtifactStorage>();
        var providerId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var service = CreateService(repository, storage);

        repository.Setup(x => x.GetProviderAsync("acme", "example"))
            .ReturnsAsync(new TerraformProvider
            {
                Id = providerId,
                Namespace = "acme",
                Type = "example",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        repository.Setup(x => x.GetProviderVersionAsync("acme", "example", "1.0.0"))
            .ReturnsAsync(new ProviderVersion
            {
                Id = versionId,
                ProviderId = providerId,
                Version = "1.0.0",
                Protocols = ["5.0"],
                KeyId = "ABCDEF",
                ShasumsStoragePath = "shasums",
                ShasumsSignatureStoragePath = "sig",
                PublishedAt = DateTime.UtcNow
            });
        repository.Setup(x => x.GetProviderPlatformAsync("acme", "example", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(new ProviderPlatform
            {
                Id = Guid.NewGuid(),
                ProviderVersionId = versionId,
                Os = "linux",
                Arch = "amd64",
                Filename = "terraform-provider-example_1.0.0_linux_amd64.zip",
                Shasum = new string('a', 64),
                PackageStoragePath = "package.zip"
            });
        repository.Setup(x => x.GetGpgKeyAsync("acme", "ABCDEF"))
            .ReturnsAsync(new ProviderGpgKey
            {
                Id = Guid.NewGuid(),
                Namespace = "acme",
                KeyId = "ABCDEF",
                AsciiArmor = "key",
                CreatedAt = DateTime.UtcNow
            });
        storage.Setup(x => x.CreateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => $"/provider/download?token={path}");

        var result = await service.GetPackageAsync("acme", "example", "1.0.0", "linux", "amd64", "127.0.0.1", "Terraform", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("linux", result!.Os);
        Assert.Equal("amd64", result.Arch);
        Assert.Equal("/provider/download?token=package.zip", result.DownloadUrl);
        Assert.Equal("/provider/download?token=shasums", result.ShasumsUrl);
        Assert.Equal("/provider/download?token=sig", result.ShasumsSignatureUrl);
        Assert.Equal("ABCDEF", Assert.Single(result.SigningKeys.GpgPublicKeys).KeyId);
        repository.Verify(x => x.RecordProviderDownloadAsync(
            providerId,
            "acme",
            "example",
            "1.0.0",
            "linux",
            "amd64",
            "127.0.0.1",
            "Terraform"), Times.Once);
    }

    [Fact]
    public async Task CreateProviderAsync_RejectsInvalidProviderNamespace()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateProviderAsync(new CreateProviderRequest
            {
                Namespace = "../acme",
                Type = "example"
            }, "user-1"));

        Assert.Contains("Invalid provider namespace", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateVersionAsync_RejectsInvalidSemanticVersion()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateVersionAsync("acme", "example", new CreateProviderVersionRequest
            {
                Version = "1",
                Protocols = ["5.0"],
                KeyId = "ABCDEF"
            }));

        Assert.Contains("Semantic Version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateVersionAsync_RejectsUnsupportedProtocols()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateVersionAsync("acme", "example", new CreateProviderVersionRequest
            {
                Version = "1.0.0",
                Protocols = ["7.0"],
                KeyId = "ABCDEF"
            }));

        Assert.Contains("Unsupported provider protocol", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadPlatformPackageAsync_ValidatesPackageAndStoresArtifact()
    {
        var repository = new Mock<IProviderRepository>();
        var storage = new Mock<IProviderArtifactStorage>();
        var validator = new Mock<IProviderPackageValidator>();
        var providerId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var platformId = Guid.NewGuid();
        var package = new MemoryStream([1, 2, 3]);
        var shasums = new MemoryStream([4]);
        var signature = new MemoryStream([5]);
        var service = CreateService(repository, storage, validator);

        repository.Setup(x => x.GetProviderAsync("acme", "example"))
            .ReturnsAsync(new TerraformProvider
            {
                Id = providerId,
                Namespace = "acme",
                Type = "example",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        repository.Setup(x => x.GetProviderVersionAsync("acme", "example", "1.0.0"))
            .ReturnsAsync(new ProviderVersion
            {
                Id = versionId,
                ProviderId = providerId,
                Version = "1.0.0",
                Protocols = ["5.0"],
                KeyId = "ABCDEF",
                ShasumsStoragePath = "shasums",
                ShasumsSignatureStoragePath = "sig",
                PublishedAt = DateTime.UtcNow
            });
        repository.Setup(x => x.GetProviderPlatformAsync("acme", "example", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(new ProviderPlatform
            {
                Id = platformId,
                ProviderVersionId = versionId,
                Os = "linux",
                Arch = "amd64",
                Filename = "terraform-provider-example_1.0.0_linux_amd64.zip",
                Shasum = new string('a', 64)
            });
        repository.Setup(x => x.GetGpgKeyAsync("acme", "ABCDEF"))
            .ReturnsAsync(new ProviderGpgKey
            {
                Id = Guid.NewGuid(),
                Namespace = "acme",
                KeyId = "ABCDEF",
                AsciiArmor = "public-key",
                CreatedAt = DateTime.UtcNow
            });
        storage.Setup(x => x.OpenReadAsync("shasums", It.IsAny<CancellationToken>()))
            .ReturnsAsync(shasums);
        storage.Setup(x => x.OpenReadAsync("sig", It.IsAny<CancellationToken>()))
            .ReturnsAsync(signature);
        storage.Setup(x => x.SaveAsync(
                "acme/example/1.0.0/linux_amd64/terraform-provider-example_1.0.0_linux_amd64.zip",
                package,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderArtifactSaveResult("stored-package", 3));
        validator.Setup(x => x.ValidatePackageAsync(
                "example",
                "1.0.0",
                "linux",
                "amd64",
                "terraform-provider-example_1.0.0_linux_amd64.zip",
                new string('a', 64),
                package,
                shasums,
                signature,
                "public-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderPackageValidationResult(true, null));
        repository.Setup(x => x.SetPlatformPackagePathAsync(platformId, "stored-package", 3))
            .ReturnsAsync(true);

        var result = await service.UploadPlatformPackageAsync("acme", "example", "1.0.0", "linux", "amd64", package, CancellationToken.None);

        Assert.True(result);
        repository.Verify(x => x.SetPlatformPackagePathAsync(platformId, "stored-package", 3), Times.Once);
    }

    private static ProviderRegistryService CreateService(
        Mock<IProviderRepository>? repository = null,
        Mock<IProviderArtifactStorage>? storage = null,
        Mock<IProviderPackageValidator>? validator = null)
    {
        return new ProviderRegistryService(
            repository?.Object ?? Mock.Of<IProviderRepository>(),
            storage?.Object ?? Mock.Of<IProviderArtifactStorage>(),
            validator?.Object ?? Mock.Of<IProviderPackageValidator>(),
            NullLogger<ProviderRegistryService>.Instance);
    }
}
