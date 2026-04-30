using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobModuleServiceDownloadTests
{
    private readonly string _containerName = "test-container";
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<BlobContainerClient> _mockContainerClient;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger;

    public AzureBlobModuleServiceDownloadTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockLogger = new Mock<ILogger<AzureBlobModuleService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockContainerClient = new Mock<BlobContainerClient>();

        // Default configuration
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
        _mockContainerClient.Setup(c => c.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, default))
            .Returns(AsyncPageable<BlobItem>.FromPages([]));
    }

    private AzureBlobModuleService CreateService()
    {
        // Pass the mocked BlobServiceClient to the constructor
        var service = new AzureBlobModuleService(
            _mockConfiguration.Object, // This mocked configuration is used
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockBlobServiceClient.Object);

        return service;
    }

    // Test: Should return null and log a warning when the module is not found in the database
    [Fact]
    public async Task GetModuleDownloadPathAsync_Returns_Null_When_Module_Not_In_Database()
    {
        // Arrange
        _mockDatabaseService.Setup(db =>
                db.GetModuleStorageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync((ModuleStorage?)null);

        var service = CreateService();

        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "prov", "1.0.0");

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found in database")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Test: Should return null and log a warning when the blob does not exist in storage, even if the module exists in the database
    [Fact]
    public async Task GetModuleDownloadPathAsync_Returns_Null_When_Blob_Does_Not_Exist()
    {
        // Arrange
        var moduleStorage = new ModuleStorage
        {
            FilePath = "path/to/blob.zip",
            Namespace = "testns", // Added
            Name = "testname", // Added
            Provider = "testprov", // Added
            Version = "1.0.0", // Added
            Description = "Test Description", // Added
            Dependencies = new List<string>() // Added
        };
        _mockDatabaseService.Setup(db =>
                db.GetModuleStorageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync(moduleStorage);

        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        // Ensure GetBlobClient is called on the _mockContainerClient (which is returned by _mockBlobServiceClient)
        _mockContainerClient.Setup(cc => cc.GetBlobClient(moduleStorage.FilePath))
            .Returns(mockBlobClient.Object);

        var service = CreateService();

        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "prov", "1.0.0");

        // Assert
        Assert.Null(result);
        mockBlobClient.Verify(bc => bc.ExistsAsync(default), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("exists in database but blob not found")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Test: Should return a SAS URI when both the module exists in the database and the blob exists in storage
    [Fact]
    public async Task GetModuleDownloadPathAsync_Returns_SasUri_When_Module_And_Blob_Exist()
    {
        // Arrange
        var moduleStorage = new ModuleStorage
        {
            FilePath = "path/to/blob.zip",
            Namespace = "testns", // Added
            Name = "testname", // Added
            Provider = "testprov", // Added
            Version = "1.0.0", // Added
            Description = "Test Description", // Added
            Dependencies = new List<string>() // Added
        };
        _mockDatabaseService.Setup(db =>
                db.GetModuleStorageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync(moduleStorage);

        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        var fakeSasUri =
            new Uri($"https://fakeaccount.blob.core.windows.net/{_containerName}/path/to/blob.zip?sastoken");
        mockBlobClient.Setup(bc => bc.GenerateSasUri(It.IsAny<BlobSasBuilder>())).Returns(fakeSasUri);
        mockBlobClient.SetupGet(bc => bc.Name).Returns(moduleStorage.FilePath);
        mockBlobClient.SetupGet(bc => bc.BlobContainerName).Returns(_containerName);


        _mockContainerClient.Setup(cc => cc.GetBlobClient(moduleStorage.FilePath))
            .Returns(mockBlobClient.Object);

        var service = CreateService();

        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "prov", "1.0.0");

        // Assert
        Assert.Equal(fakeSasUri.ToString(), result);
        mockBlobClient.Verify(bc => bc.GenerateSasUri(It.Is<BlobSasBuilder>(bsb =>
                bsb.BlobName == moduleStorage.FilePath &&
                bsb.BlobContainerName == _containerName &&
                bsb.Resource == "b" &&
                bsb.Permissions.Contains("r") // Changed from bsb.GetPermissions().HasFlag(BlobSasPermissions.Read)
        )), Times.Once);
    }

    // Test: Should handle exceptions during SAS generation, log an error, and return null
    [Fact]
    public async Task GetModuleDownloadPathAsync_Handles_Unexpected_Exception_During_Sas_Generation_And_Returns_Null()
    {
        // Arrange
        var moduleStorage = new ModuleStorage
        {
            FilePath = "path/to/blob.zip",
            Namespace = "testns", // Added
            Name = "testname", // Added
            Provider = "testprov", // Added
            Version = "1.0.0", // Added
            Description = "Test Description", // Added
            Dependencies = new List<string>() // Added
        };
        _mockDatabaseService.Setup(db =>
                db.GetModuleStorageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync(moduleStorage);

        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        mockBlobClient.Setup(bc => bc.GenerateSasUri(It.IsAny<BlobSasBuilder>()))
            .Throws(new Exception("SAS error"));

        _mockContainerClient.Setup(cc => cc.GetBlobClient(moduleStorage.FilePath))
            .Returns(mockBlobClient.Object);

        var service = CreateService();

        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "prov", "1.0.0");

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error generating SAS token")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsync_FallsBack_ToBlobUri_When_SasGeneration_NotSupported()
    {
        var moduleStorage = new ModuleStorage
        {
            FilePath = "path/to/blob.zip",
            Namespace = "testns",
            Name = "testname",
            Provider = "testprov",
            Version = "1.0.0",
            Description = "Test Description",
            Dependencies = new List<string>()
        };
        _mockDatabaseService.Setup(db =>
                db.GetModuleStorageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync(moduleStorage);

        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        mockBlobClient.Setup(bc => bc.GenerateSasUri(It.IsAny<BlobSasBuilder>()))
            .Throws(new InvalidOperationException("SAS not supported"));
        mockBlobClient.SetupGet(bc => bc.Uri)
            .Returns(new Uri($"https://fakeaccount.blob.core.windows.net/{_containerName}/{moduleStorage.FilePath}"));

        _mockContainerClient.Setup(cc => cc.GetBlobClient(moduleStorage.FilePath))
            .Returns(mockBlobClient.Object);

        var service = CreateService();

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "prov", "1.0.0");

        Assert.Equal(mockBlobClient.Object.Uri.ToString(), result);
    }
}
