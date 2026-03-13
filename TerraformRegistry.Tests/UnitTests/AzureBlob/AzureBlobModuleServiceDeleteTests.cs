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

public class AzureBlobModuleServiceDeleteTests
{
    private const string ContainerName = "test-container";
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<BlobContainerClient> _mockContainerClient = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger = new();

    public AzureBlobModuleServiceDeleteTests()
    {
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
    public async Task DeleteModuleAsync_Removes_Db_Entry_And_Blob()
    {
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-provider-1.0.0.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };

        var blobClient = new Mock<BlobClient>();
        blobClient.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient
            .Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _mockContainerClient.Setup(x => x.GetBlobClient(storage.FilePath)).Returns(blobClient.Object);
        _mockDatabaseService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        _mockDatabaseService.Setup(x => x.RemoveModuleAsync(storage)).ReturnsAsync(true);

        var service = CreateService();
        var result = await service.DeleteModuleAsync("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(storage), Times.Once);
        blobClient.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default),
            Times.Once);
    }

    [Fact]
    public async Task DeleteModuleAsync_Cleans_Orphaned_Db_Entry_When_Blob_Missing()
    {
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-provider-1.0.0.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };

        var blobClient = new Mock<BlobClient>();
        blobClient.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        _mockContainerClient.Setup(x => x.GetBlobClient(storage.FilePath)).Returns(blobClient.Object);
        _mockDatabaseService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        _mockDatabaseService.Setup(x => x.RemoveModuleAsync(storage)).ReturnsAsync(true);

        var service = CreateService();
        var result = await service.DeleteModuleAsync("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(storage), Times.Once);
    }

    [Fact]
    public async Task DeleteModuleAsync_Cleans_Orphaned_Blob_When_Db_Entry_Missing()
    {
        var blobClient = new Mock<BlobClient>();
        blobClient.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient
            .Setup(x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _mockContainerClient.Setup(x => x.GetBlobClient("ns/name-provider-1.0.0.zip")).Returns(blobClient.Object);
        _mockDatabaseService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);

        var service = CreateService();
        var result = await service.DeleteModuleAsync("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        blobClient.Verify(
            x => x.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default),
            Times.Once);
    }
}
