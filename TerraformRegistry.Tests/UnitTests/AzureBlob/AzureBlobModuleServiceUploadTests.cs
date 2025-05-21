using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Models;

// Required for Response<T>

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobModuleServiceUploadTests
{
    private readonly string _containerName = "test-container";
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient; // Added
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<BlobContainerClient> _mockContainerClient;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger;


    public AzureBlobModuleServiceUploadTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockLogger = new Mock<ILogger<AzureBlobModuleService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockBlobServiceClient = new Mock<BlobServiceClient>(); // Initialized
        _mockContainerClient = new Mock<BlobContainerClient>();

        var mockAzureStorageSection = new Mock<IConfigurationSection>();
        _mockConfiguration.Setup(c => c.GetSection("AzureStorage")).Returns(mockAzureStorageSection.Object);
        _mockConfiguration.Setup(c => c["AzureStorage:ContainerName"]).Returns(_containerName);
        _mockConfiguration.Setup(c => c["AzureStorage:SasTokenExpiryMinutes"]).Returns("5");

        // Setup the service client to return the mocked container client
        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(_containerName))
            .Returns(_mockContainerClient.Object);

        // Setup CreateIfNotExists for the constructor path, called on the _mockContainerClient
        _mockContainerClient.Setup(c =>
                c.CreateIfNotExists(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), default))
            .Returns(Mock.Of<Response<BlobContainerInfo>>());
    }

    private AzureBlobModuleService CreateService()
    {
        // Pass the mocked BlobServiceClient to the constructor
        var service = new AzureBlobModuleService(
            _mockConfiguration.Object,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockBlobServiceClient.Object); // Pass the mock client
        return service;
    }

    // Test: Should return false and log a warning if the blob already exists in storage
    [Fact]
    public async Task UploadModuleAsync_Returns_False_If_Blob_Already_Exists()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Never);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already exists in blob storage")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Test: Should return true on successful upload to blob storage and successful database add
    [Fact]
    public async Task UploadModuleAsync_Returns_True_On_Successful_Upload_And_Db_Add()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        _mockDatabaseService.Setup(db => db.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.True(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Once);
        _mockDatabaseService.Verify(db => db.AddModuleAsync(It.Is<ModuleStorage>(m =>
            m.Namespace == "ns" && m.Name == "name" && m.Provider == "prov" && m.Version == "1.0.0"
        )), Times.Once);
    }

    // Test: Should delete the blob and return false if the database add fails after upload
    [Fact]
    public async Task UploadModuleAsync_Deletes_Blob_And_Returns_False_If_Db_Add_Fails()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        mockBlobClient.Setup(bc =>
                bc.DeleteAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Mock.Of<Response>());


        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        _mockDatabaseService.Setup(db => db.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false); // Simulate database add failure

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Once);
        mockBlobClient.Verify(bc => bc.DeleteAsync(DeleteSnapshotsOption.None, null, default),
            Times.Once); // Verify cleanup
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Failed to add module") &&
                    v.ToString()!.Contains("to database, cleaned up blob storage")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Test: Should handle exceptions during blob upload, attempt cleanup, and return false
    [Fact]
    public async Task UploadModuleAsync_Handles_Exception_During_Blob_Upload_And_Cleans_Up()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        // First ExistsAsync returns false (blob does not exist)
        mockBlobClient.SetupSequence(bc => bc.ExistsAsync(default))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>())) // For initial check
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>())); // For cleanup check if upload fails mid-way

        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ThrowsAsync(new RequestFailedException("Upload failed"));
        mockBlobClient.Setup(bc =>
                bc.DeleteAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Mock.Of<Response>());


        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.DeleteAsync(DeleteSnapshotsOption.None, null, default),
            Times.Once); // Verify cleanup attempt
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error uploading module")),
                It.IsAny<RequestFailedException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}