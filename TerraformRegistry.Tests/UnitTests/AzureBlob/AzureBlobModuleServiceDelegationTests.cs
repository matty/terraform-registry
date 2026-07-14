using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobModuleServiceDelegationTests
{
    private const string ContainerName = "test-container";
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<BlobContainerClient> _mockContainerClient = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger = new();

    public AzureBlobModuleServiceDelegationTests()
    {
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockConfiguration.Setup(c => c["AzureStorage:ContainerName"]).Returns(ContainerName);
        _mockConfiguration.Setup(c => c["AzureStorage:SasTokenExpiryMinutes"]).Returns("5");
        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(ContainerName)).Returns(_mockContainerClient.Object);
        _mockContainerClient
            .Setup(c => c.CreateIfNotExists(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(),
                default)).Returns(Mock.Of<Response<BlobContainerInfo>>());
    }

    private AzureBlobModuleService CreateService()
    {
        return new AzureBlobModuleService(
            _mockConfiguration.Object,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockBlobServiceClient.Object);
    }

    [Fact]
    public async Task ListModulesAsyncDelegatesToDatabaseService()
    {
        var request = new ModuleSearchRequest();
        var expected = new ModuleList
        {
            Modules = new List<ModuleListItem>(),
            Meta = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        _mockDatabaseService.Setup(x => x.ListModulesAsync(request)).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.ListModulesAsync(request);
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.ListModulesAsync(request), Times.Once);
    }

    [Fact]
    public async Task GetModuleAsyncDelegatesToDatabaseService()
    {
        var expected = new TerraformModule
        {
            Id = string.Empty,
            Owner = string.Empty,
            Namespace = string.Empty,
            Name = string.Empty,
            Version = string.Empty,
            Provider = string.Empty,
            PublishedAt = string.Empty,
            Versions = new List<string>(),
            Root = string.Empty,
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        _mockDatabaseService.Setup(x => x.GetModuleAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.GetModuleAsync("ns", "name", "provider", "1.0.0");
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.GetModuleAsync("ns", "name", "provider", "1.0.0"), Times.Once);
    }

    [Fact]
    public async Task GetModuleVersionsAsyncDelegatesToDatabaseService()
    {
        var expected = new ModuleVersions
        {
            Modules = new List<ModuleVersionInfo>
            {
                new ModuleVersionInfo
                {
                    Versions = new List<VersionInfo>()
                }
            }
        };
        _mockDatabaseService.Setup(x => x.GetModuleVersionsAsync("ns", "name", "provider")).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.GetModuleVersionsAsync("ns", "name", "provider");
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.GetModuleVersionsAsync("ns", "name", "provider"), Times.Once);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncUsesDelegationKeyOptionsWhenAccountKeyIsUnavailable()
    {
        var moduleStorage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "description",
            FilePath = "path/to/module.zip",
            Dependencies = []
        };
        var blobClient = new Mock<BlobClient>();
        var expectedUri = new Uri("https://example.test/path/to/module.zip?sig=test");

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync(moduleStorage);
        _mockContainerClient.Setup(x => x.GetBlobClient(moduleStorage.FilePath)).Returns(blobClient.Object);
        blobClient.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient.SetupGet(x => x.CanGenerateSasUri).Returns(false);
        _mockBlobServiceClient
            .Setup(x => x.GetUserDelegationKeyAsync(
                It.Is<BlobGetUserDelegationKeyOptions>(options =>
                    options.StartsOn <= DateTimeOffset.UtcNow &&
                    options.StartsOn >= DateTimeOffset.UtcNow.AddMinutes(-6) &&
                    options.ExpiresOn > DateTimeOffset.UtcNow),
                CancellationToken.None))
            .ReturnsAsync(Response.FromValue<UserDelegationKey>(null!, Mock.Of<Response>()));
        blobClient.Setup(x => x.GenerateUserDelegationSasUri(
                It.IsAny<Azure.Storage.Sas.BlobSasBuilder>(), It.IsAny<UserDelegationKey>()))
            .Returns(expectedUri);

        var result = await CreateService().GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");

        Assert.Equal(expectedUri.ToString(), result);
        _mockBlobServiceClient.Verify(x => x.GetUserDelegationKeyAsync(
            It.Is<BlobGetUserDelegationKeyOptions>(options =>
                options.StartsOn <= DateTimeOffset.UtcNow &&
                options.StartsOn >= DateTimeOffset.UtcNow.AddMinutes(-6) &&
                options.ExpiresOn > DateTimeOffset.UtcNow),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncForwardsCancellationToDatabaseBlobAndDelegationKey()
    {
        using var cancellation = new CancellationTokenSource();
        var moduleStorage = new ModuleStorage
        {
            Namespace = "ns", Name = "name", Provider = "provider", Version = "1.0.0", Description = "description",
            FilePath = "path/to/module.zip", Dependencies = []
        };
        var blobClient = new Mock<BlobClient>();
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0", cancellation.Token))
            .ReturnsAsync(moduleStorage);
        _mockContainerClient.Setup(x => x.GetBlobClient(moduleStorage.FilePath)).Returns(blobClient.Object);
        blobClient.Setup(x => x.ExistsAsync(cancellation.Token))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient.SetupGet(x => x.CanGenerateSasUri).Returns(false);
        _mockBlobServiceClient
            .Setup(x => x.GetUserDelegationKeyAsync(It.IsAny<BlobGetUserDelegationKeyOptions>(), cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService().GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0", cancellation.Token));

        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0", cancellation.Token), Times.Once);
        blobClient.Verify(x => x.ExistsAsync(cancellation.Token), Times.Once);
        _mockBlobServiceClient.Verify(
            x => x.GetUserDelegationKeyAsync(It.IsAny<BlobGetUserDelegationKeyOptions>(), cancellation.Token), Times.Once);
    }
}
