using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.UnitTests;

public class ProviderRegistryServiceTests
{
    [Fact]
    public async Task GetVersionsAsyncReturnsNullWhenRepositoryReturnsNoVersions()
    {
        var repository = new Mock<IProviderRepository>();
        var service = CreateService(repository);
        repository.Setup(x => x.GetProviderVersionsAsync("acme", "example"))
            .ReturnsAsync([]);

        var result = await service.GetVersionsAsync("acme", "example");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPackageAsyncReturnsPackageResponseWithSignedAssetUrlsAndRecordsDownload()
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
        repository.Setup(x => x.GetProviderPackageDetailsAsync("acme", "example", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(new ProviderPackageDetails(
                providerId,
                ["5.0"],
                "ABCDEF",
                "shasums",
                "sig",
                "linux",
                "amd64",
                "terraform-provider-example_1.0.0_linux_amd64.zip",
                new string('a', 64),
                "package.zip",
                "key",
                null,
                null,
                null));
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
    public async Task GetPackageAsyncUsesOneRepositoryReadAndCreatesArtifactUrlsConcurrently()
    {
        var repository = new Mock<IProviderRepository>(MockBehavior.Strict);
        var storage = new Mock<IProviderArtifactStorage>(MockBehavior.Strict);
        var packageDetails = new ProviderPackageDetails(
            Guid.NewGuid(),
            ["5.0"],
            "ABCDEF",
            "shasums",
            "signature",
            "linux",
            "amd64",
            "terraform-provider-example_1.0.0_linux_amd64.zip",
            new string('a', 64),
            "package.zip",
            "key",
            null,
            null,
            null);
        repository
            .Setup(x => x.GetProviderPackageDetailsAsync("acme", "example", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(packageDetails);
        repository
            .Setup(x => x.RecordProviderDownloadAsync(packageDetails.ProviderId, "acme", "example", "1.0.0", "linux", "amd64", null, null))
            .Returns(Task.CompletedTask);

        var urlsStarted = 0;
        var releaseUrls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage
            .Setup(x => x.CreateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string path, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref urlsStarted) == 3)
                    releaseUrls.SetResult();

                await releaseUrls.Task.WaitAsync(cancellationToken);
                return $"/provider/download?token={path}";
            });
        var service = CreateService(repository, storage);

        var response = await service.GetPackageAsync("acme", "example", "1.0.0", "linux", "amd64", null, null, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(3, urlsStarted);
        repository.Verify(x => x.GetProviderPackageDetailsAsync("acme", "example", "1.0.0", "linux", "amd64"), Times.Once);
        repository.Verify(x => x.RecordProviderDownloadAsync(packageDetails.ProviderId, "acme", "example", "1.0.0", "linux", "amd64", null, null), Times.Once);
    }

    [Fact]
    public async Task CreateProviderAsyncRejectsInvalidProviderNamespace()
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
    public async Task CreateVersionAsyncRejectsInvalidSemanticVersion()
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
    public async Task CreateVersionAsyncRejectsUnsupportedProtocols()
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
    public async Task CreateVersionAsyncAcceptsDocumentedProviderProtocolMinorVersions()
    {
        var repository = new Mock<IProviderRepository>();
        var providerId = Guid.NewGuid();
        var service = CreateService(repository);
        repository.Setup(x => x.GetProviderAsync("acme", "example"))
            .ReturnsAsync(new TerraformProvider
            {
                Id = providerId,
                Namespace = "acme",
                Type = "example",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
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
        repository.Setup(x => x.CreateProviderVersionAsync(providerId, "1.0.0", It.Is<string[]>(p => p.SequenceEqual(new[] { "5.0", "5.2", "6.1" })), "ABCDEF"))
            .ReturnsAsync(new ProviderVersion
            {
                Id = Guid.NewGuid(),
                ProviderId = providerId,
                Version = "1.0.0",
                Protocols = ["5.0", "5.2", "6.1"],
                KeyId = "ABCDEF",
                PublishedAt = DateTime.UtcNow
            });

        var version = await service.CreateVersionAsync("acme", "example", new CreateProviderVersionRequest
        {
            Version = "1.0.0",
            Protocols = ["5.0", "5.2", "6.1"],
            KeyId = "ABCDEF"
        });

        Assert.Equal(["5.0", "5.2", "6.1"], version.Protocols);
    }

    [Fact]
    public async Task RevokeGpgKeyAsyncRejectsKeyUsedByActiveProviderVersions()
    {
        var repository = new Mock<IProviderRepository>();
        var service = CreateService(repository);
        repository.Setup(x => x.GetGpgKeyAsync("acme", "ABCDEF"))
            .ReturnsAsync(new ProviderGpgKey
            {
                Id = Guid.NewGuid(),
                Namespace = "acme",
                KeyId = "ABCDEF",
                AsciiArmor = "public-key",
                CreatedAt = DateTime.UtcNow
            });
        repository.Setup(x => x.ProviderGpgKeyIsReferencedByActiveVersionsAsync("acme", "ABCDEF"))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeGpgKeyAsync("acme", "ABCDEF"));

        Assert.Contains("active provider versions", ex.Message, StringComparison.Ordinal);
        repository.Verify(x => x.RevokeGpgKeyAsync("acme", "ABCDEF"), Times.Never);
    }

    [Fact]
    public async Task DeleteProviderAsyncRemovesStoredArtifactsAfterRepositoryDelete()
    {
        var repository = new Mock<IProviderRepository>();
        var storage = new Mock<IProviderArtifactStorage>();
        var service = CreateService(repository, storage);
        repository.Setup(x => x.GetProviderArtifactStoragePathsAsync("acme", "example", null, null, null))
            .ReturnsAsync(["shasums", "shasums.sig", "linux.zip"]);
        repository.Setup(x => x.DeleteProviderAsync("acme", "example"))
            .ReturnsAsync(true);
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var deleted = await service.DeleteProviderAsync("acme", "example");

        Assert.True(deleted);
        storage.Verify(x => x.DeleteAsync("shasums", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.DeleteAsync("shasums.sig", It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.DeleteAsync("linux.zip", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadPlatformPackageAsyncValidatesPackageAndStoresArtifact()
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
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderArtifactSaveResult("stored-package", 3));
        validator.Setup(x => x.ValidatePackageAsync(
                "example",
                "1.0.0",
                "linux",
                "amd64",
                "terraform-provider-example_1.0.0_linux_amd64.zip",
                new string('a', 64),
                It.IsAny<Stream>(),
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

    [Fact]
    public async Task UploadPlatformPackageAsyncStoresValidatedNonSeekableUploadBody()
    {
        var repository = new Mock<IProviderRepository>();
        var storage = new Mock<IProviderArtifactStorage>();
        var validator = new Mock<IProviderPackageValidator>();
        var providerId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var platformId = Guid.NewGuid();
        var package = new NonSeekableReadStream([1, 2, 3]);
        var shasums = new MemoryStream([4]);
        var signature = new MemoryStream([5]);
        byte[] savedBytes = [];
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
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, Stream content, CancellationToken cancellationToken) =>
            {
                using var saved = new MemoryStream();
                await content.CopyToAsync(saved, cancellationToken);
                savedBytes = saved.ToArray();
                return new ProviderArtifactSaveResult("stored-package", savedBytes.Length);
            });
        validator.Setup(x => x.ValidatePackageAsync(
                "example",
                "1.0.0",
                "linux",
                "amd64",
                "terraform-provider-example_1.0.0_linux_amd64.zip",
                new string('a', 64),
                It.IsAny<Stream>(),
                shasums,
                signature,
                "public-key",
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string _, string _, string _, string _, Stream content, Stream _, Stream _, string _, CancellationToken cancellationToken) =>
            {
                using var validated = new MemoryStream();
                await content.CopyToAsync(validated, cancellationToken);
                Assert.Equal([1, 2, 3], validated.ToArray());
                return new ProviderPackageValidationResult(true, null);
            });
        repository.Setup(x => x.SetPlatformPackagePathAsync(platformId, "stored-package", 3))
            .ReturnsAsync(true);

        var result = await service.UploadPlatformPackageAsync("acme", "example", "1.0.0", "linux", "amd64", package, CancellationToken.None);

        Assert.True(result);
        Assert.Equal([1, 2, 3], savedBytes);
        repository.Verify(x => x.SetPlatformPackagePathAsync(platformId, "stored-package", 3), Times.Once);
    }

    [Fact]
    public async Task UploadPlatformPackageAsyncRejectsOversizedNonSeekableUploadBody()
    {
        var repository = new Mock<IProviderRepository>();
        var storage = new Mock<IProviderArtifactStorage>(MockBehavior.Strict);
        var validator = new Mock<IProviderPackageValidator>(MockBehavior.Strict);
        var tempDir = Directory.CreateTempSubdirectory();
        using var package = new NonSeekableReadStream([1, 2, 3, 4, 5]);
        var service = CreateService(repository, storage, validator, new ProviderUploadOptions
        {
            MaxPackageBytes = 4,
            TempRoot = tempDir.FullName
        });
        SetUpUploadPrerequisites(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadPlatformPackageAsync("acme", "example", "1.0.0", "linux", "amd64", package, CancellationToken.None));

        Assert.Contains("exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
        validator.VerifyNoOtherCalls();
        storage.VerifyNoOtherCalls();
        Assert.Empty(Directory.EnumerateFiles(tempDir.FullName));
    }

    private static ProviderRegistryService CreateService(
        Mock<IProviderRepository>? repository = null,
        Mock<IProviderArtifactStorage>? storage = null,
        Mock<IProviderPackageValidator>? validator = null,
        ProviderUploadOptions? uploadOptions = null)
    {
        return new ProviderRegistryService(
            repository?.Object ?? Mock.Of<IProviderRepository>(),
            storage?.Object ?? Mock.Of<IProviderArtifactStorage>(),
            validator?.Object ?? Mock.Of<IProviderPackageValidator>(),
            NullLogger<ProviderRegistryService>.Instance,
            uploadOptions);
    }

    private static void SetUpUploadPrerequisites(Mock<IProviderRepository> repository)
    {
        var providerId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
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
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] content)
        {
            _inner = new MemoryStream(content);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
