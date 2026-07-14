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
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
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

    // Test: Should return false before staging when the catalog already owns the coordinate.
    [Fact]
    public async Task UploadModuleAsyncReturnsFalseWhenCatalogAlreadyOwnsCoordinate()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync(new ModuleStorage
            {
                Namespace = "ns",
                Name = "name",
                Provider = "prov",
                Version = "1.0.0",
                Description = "desc",
                FilePath = "publications/winner/module.zip",
                PublishedAt = DateTime.UtcNow,
                Dependencies = []
            });

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Never);
    }

    // Test: Should return true on successful upload and transactional catalog commit.
    [Fact]
    public async Task UploadModuleAsyncReturnsTrueAfterTransactionalCatalogCommit()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.True(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Once);
        _mockDatabaseService.Verify(db => db.TryCommitStagedPublicationAsync(It.IsAny<ModulePublicationAttempt>(), It.Is<ModuleStorage>(m =>
            m.Namespace == "ns" && m.Name == "name" && m.Provider == "prov" && m.Version == "1.0.0"
        ), null), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsyncKeepsCommittedBlobWhenRequestIsCanceledAfterCommit()
    {
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        mockBlobClient.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null, cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ReturnsAsync(true);
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc",
            cancellationToken: cancellation.Token);

        Assert.True(result);
        mockBlobClient.Verify(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDatabaseService.Verify(db => db.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncStagesAnAttemptOwnedBlobAndCommitsTheCatalog()
    {
        var blobPaths = new List<string>();
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.ExistsAsync(default))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(blobPaths.Add)
            .Returns(mockBlobClient.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .ReturnsAsync(true);
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        Assert.True(result);
        var path = Assert.Single(blobPaths);
        Assert.Contains("publications/", path, StringComparison.Ordinal);
        Assert.Contains("name-prov-1.0.0.zip", path, StringComparison.Ordinal);
        _mockDatabaseService.Verify(db => db.TryCommitStagedPublicationAsync(
            It.Is<ModulePublicationAttempt>(attempt => attempt.State == ModulePublicationAttemptState.Staged),
            It.Is<ModuleStorage>(module => module.FilePath == path), null), Times.Once);
        _mockDatabaseService.Verify(db => db.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    // Test: Should delete the attempt-owned blob and return false if the catalog commit loses.
    [Fact]
    public async Task UploadModuleAsyncDeletesBlobAndReturnsFalseIfDbAddFails()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        mockBlobClient.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));


        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .ReturnsAsync(false);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default),
            Times.Once);
        mockBlobClient.Verify(bc => bc.DeleteIfExistsAsync(DeleteSnapshotsOption.None, null, default), Times.Once);
    }

    // Test: Should handle exceptions during blob upload, attempt cleanup, and return false
    [Fact]
    public async Task UploadModuleAsyncHandlesExceptionDuringBlobUploadAndCleansUp()
    {
        // Arrange
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ThrowsAsync(new RequestFailedException("Upload failed"));
        mockBlobClient.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));


        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc");

        // Assert
        Assert.False(result);
        mockBlobClient.Verify(bc => bc.DeleteIfExistsAsync(DeleteSnapshotsOption.None, null, default),
            Times.Once); // Verify cleanup attempt
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error publishing module")),
                It.Is<Exception?>(exception => exception != null &&
                    exception.Message.Contains("RequestFailedException: Upload failed", StringComparison.Ordinal)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncDeletesBlobBeforeRemovingCatalogRow()
    {
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        _mockContainerClient.Setup(cc => cc.GetBlobClient("publications/attempt/module.zip"))
            .Returns(mockBlobClient.Object);
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "prov",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "publications/attempt/module.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        _mockDatabaseService.Setup(db => db.GetModuleStorageIncludingDeletedAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService.Setup(db => db.RemoveModuleAsync(module))
            .Callback(() => mockBlobClient.Verify(bc => bc.DeleteIfExistsAsync(
                DeleteSnapshotsOption.None, null, default), Times.Once))
            .ReturnsAsync(true);
        var service = CreateService();

        var result = await service.PurgeModuleVersionAsync("ns", "name", "prov", "1.0.0");

        Assert.True(result);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncKeepsCatalogRowWhenBlobDeletionFails()
    {
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), default))
            .ThrowsAsync(new RequestFailedException("Delete failed"));
        _mockContainerClient.Setup(cc => cc.GetBlobClient("publications/attempt/module.zip"))
            .Returns(mockBlobClient.Object);
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "prov",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "publications/attempt/module.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        _mockDatabaseService.Setup(db => db.GetModuleStorageIncludingDeletedAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync(module);
        var service = CreateService();

        var result = await service.PurgeModuleVersionAsync("ns", "name", "prov", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(db => db.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncCleansOnlyItsAttemptBlobWhenCatalogCommitLoses()
    {
        var attemptBlob = new Mock<BlobClient>();
        attemptBlob.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        attemptBlob.Setup(bc => bc.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(), default))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        var winner = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "prov",
            Version = "1.0.0",
            Description = "winner",
            FilePath = "publications/winner/module.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>())).Returns(attemptBlob.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync(winner);
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), winner))
            .ReturnsAsync(false);
        _mockDatabaseService.Setup(db => db.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        attemptBlob.Verify(bc => bc.DeleteIfExistsAsync(DeleteSnapshotsOption.None, null, default), Times.Once);
        _mockContainerClient.Verify(cc => cc.GetBlobClient(winner.FilePath), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncCommitsReplacementMetadataInsteadOfStaleCatalogMetadata()
    {
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(bc => bc.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
        _mockContainerClient.Setup(cc => cc.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        var existing = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "prov",
            Version = "1.0.0",
            Description = "old",
            FilePath = "publications/winner/module.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = [],
            Metadata = new ModuleArtifactMetadata { RootSubdirectory = "old" }
        };
        var replacementMetadata = new ModuleArtifactMetadata { RootSubdirectory = "replacement" };
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("ns", "name", "prov", "1.0.0"))
            .ReturnsAsync(existing);
        _mockDatabaseService.Setup(db => db.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _mockDatabaseService.Setup(db => db.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), existing))
            .ReturnsAsync(true);
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "prov", "1.0.0", stream, "replacement",
            replace: true, metadata: replacementMetadata);

        Assert.True(result);
        _mockDatabaseService.Verify(db => db.TryCommitStagedPublicationAsync(
            It.IsAny<ModulePublicationAttempt>(),
            It.Is<ModuleStorage>(module => module.Description == "replacement" && module.Metadata == replacementMetadata),
            existing), Times.Once);
    }
}
