using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobModuleServiceConstructorTests
{
    private readonly string _containerName = "test-container";
    private readonly Mock<BlobContainerClient> _mockBlobContainerClient;
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger;

    public AzureBlobModuleServiceConstructorTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockLogger = new Mock<ILogger<AzureBlobModuleService>>();
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockBlobContainerClient = new Mock<BlobContainerClient>();

        // Setup default behavior for mocks
        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(_mockBlobContainerClient.Object);
        _mockBlobContainerClient.Setup(c =>
                c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());
        _mockBlobContainerClient.Setup(c => c.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, default))
            .Returns(AsyncPageable<BlobItem>.FromPages([]));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? azureStorageSettings)
    {
        var configBuilder = new ConfigurationBuilder();
        if (azureStorageSettings != null)
        {
            configBuilder.AddInMemoryCollection(
                azureStorageSettings.Select(kvp =>
                    new KeyValuePair<string, string?>($"AzureStorage:{kvp.Key}", kvp.Value))
            );
        }

        return configBuilder.Build();
    }

    // Storage I/O is deferred until hosted startup has completed database migration.
    [Fact]
    public async Task InitializationCreatesContainerAfterSideEffectFreeConstruction()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName },
            { "SasTokenExpiryMinutes", "5" }
        };
        var configuration = CreateConfiguration(settings);

        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(_containerName))
            .Returns(_mockBlobContainerClient.Object);

        // Act
        var service = new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
            _mockBlobServiceClient.Object);

        // Assert
        Assert.NotNull(service);
        _mockBlobServiceClient.Verify(s => s.GetBlobContainerClient(_containerName), Times.Once);
        _mockBlobContainerClient.Verify(
            c => c.CreateIfNotExistsAsync(PublicAccessType.None, null, null, It.IsAny<CancellationToken>()), Times.Never);

        await service.InitializeStorageAsync(CancellationToken.None);

        _mockBlobContainerClient.Verify(
            c => c.CreateIfNotExistsAsync(PublicAccessType.None, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Test: Should throw ArgumentNullException if ContainerName is missing from configuration
    [Fact]
    public void ConstructorThrowsArgumentNullExceptionForMissingContainerName()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            // ContainerName is missing
            { "SasTokenExpiryMinutes", "5" }
        };
        var configuration = CreateConfiguration(settings);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
                _mockBlobServiceClient.Object));
        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("AzureStorage:ContainerName", ex.Message, StringComparison.Ordinal);
    }

    // Test: Should use the default value for SasTokenExpiryMinutes if it is missing from configuration
    [Fact]
    public void ConstructorUsesDefaultSasTokenExpiryMinutesWhenMissing()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName }
            // SasTokenExpiryMinutes is missing, should default to "5"
        };
        var configuration = CreateConfiguration(settings);

        // Act
        var service = new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
            _mockBlobServiceClient.Object);

        // Assert
        Assert.NotNull(service); // Ensures constructor completes and default is parsed
        // To verify the actual value, one would typically need to expose it or test a method that uses it.
        // For this constructor test, we're primarily ensuring it doesn't throw with missing optional config.
    }

    // Test: Should throw FormatException if SasTokenExpiryMinutes is not a valid integer
    [Fact]
    public void ConstructorThrowsFormatExceptionForInvalidSasTokenExpiryMinutes()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName },
            { "SasTokenExpiryMinutes", "not-an-integer" }
        };
        var configuration = CreateConfiguration(settings);

        // Act & Assert
        Assert.Throws<FormatException>(() =>
            new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
                _mockBlobServiceClient.Object));
    }

    // Test: Should throw ArgumentNullException if both connection string and account name are missing
    [Fact]
    public void ConstructorThrowsArgumentNullExceptionIfConnectionStringAndAccountNameMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("AzureStorage:ContainerName", _containerName)
                // No ConnectionString, no AccountName
            })
            .Build();

        // Use a real BlobServiceClient mock that will not be used (to force the else branch)
        // Pass null for blobServiceClient to hit the Managed Identity path
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new AzureBlobModuleService(config, _mockDatabaseService.Object, _mockLogger.Object));
        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("AzureStorage:AccountName", ex.Message, StringComparison.Ordinal);
        _mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Azure Storage AccountName")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // Test: Storage initialization propagates container failures.
    [Fact]
    public async Task InitializationRethrowsIfCreateIfNotExistsFails()
    {
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName },
            { "SasTokenExpiryMinutes", "5" }
        };
        var configuration = CreateConfiguration(settings);
        var testException = new InvalidOperationException("fail");
        _mockBlobContainerClient.Setup(c =>
                c.CreateIfNotExistsAsync(PublicAccessType.None, null, null, It.IsAny<CancellationToken>()))
            .Throws(testException);
        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(_containerName))
            .Returns(_mockBlobContainerClient.Object);
        var service = new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
            _mockBlobServiceClient.Object);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeStorageAsync(CancellationToken.None));
        Assert.Equal(testException, ex);
    }

    [Fact]
    public async Task InitializationSynchronizesExistingBlobsIntoDatabase()
    {
        var settings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName },
            { "SasTokenExpiryMinutes", "5" }
        };
        var configuration = CreateConfiguration(settings);

        var blobName = "acme/network-aws-1.10.0.zip";
        var blobItem = BlobsModelFactory.BlobItem(blobName, false, null, null, null);
        var page = Page<BlobItem>.FromValues([blobItem], null, Mock.Of<Response>());
        var blobs = AsyncPageable<BlobItem>.FromPages([page]);

        var mockBlobClient = new Mock<BlobClient>();
        var properties = BlobsModelFactory.BlobProperties(lastModified: DateTimeOffset.UtcNow, metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        mockBlobClient.Setup(b => b.GetPropertiesAsync(null, default))
            .ReturnsAsync(Response.FromValue(properties, Mock.Of<Response>()));

        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(_containerName))
            .Returns(_mockBlobContainerClient.Object);
        _mockBlobContainerClient.Setup(c => c.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, default))
            .Returns(blobs);
        _mockBlobContainerClient.Setup(c => c.GetBlobClient(blobName))
            .Returns(mockBlobClient.Object);
        _mockDatabaseService.Setup(db => db.GetModuleStorageAsync("acme", "network", "aws", "1.10.0"))
            .ReturnsAsync((TerraformRegistry.Models.ModuleStorage?)null);
        _mockDatabaseService.Setup(db => db.AddModuleAsync(It.IsAny<TerraformRegistry.Models.ModuleStorage>()))
            .ReturnsAsync(true);

        var service = new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
            _mockBlobServiceClient.Object);

        _mockDatabaseService.Verify(db => db.AddModuleAsync(It.IsAny<TerraformRegistry.Models.ModuleStorage>()), Times.Never);
        await service.InitializeStorageAsync(CancellationToken.None);

        Assert.NotNull(service);
        _mockDatabaseService.Verify(db => db.AddModuleAsync(It.Is<TerraformRegistry.Models.ModuleStorage>(m =>
            m.Namespace == "acme" &&
            m.Name == "network" &&
            m.Provider == "aws" &&
            m.Version == "1.10.0" &&
            m.FilePath == blobName
        )), Times.Once);
    }

    [Fact]
    public async Task InitializationDoesNotImportStagedPublicationBlobs()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "ContainerName", _containerName },
            { "SasTokenExpiryMinutes", "5" }
        };
        var configuration = CreateConfiguration(settings);
        const string stagedBlobName = "publications/5f7d39dc3bb84fef8b61cf0e84b33117/network-aws-1.10.0.zip";
        var stagedBlob = BlobsModelFactory.BlobItem(stagedBlobName, false, null, null, null);
        var page = Page<BlobItem>.FromValues([stagedBlob], null, Mock.Of<Response>());
        _mockBlobContainerClient.Setup(c => c.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, default))
            .Returns(AsyncPageable<BlobItem>.FromPages([page]));

        var service = new AzureBlobModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object,
            _mockBlobServiceClient.Object);

        await service.InitializeStorageAsync(CancellationToken.None);

        _mockBlobContainerClient.Verify(c => c.GetBlobClient(stagedBlobName), Times.Never);
        _mockDatabaseService.Verify(db => db.AddModuleAsync(It.IsAny<TerraformRegistry.Models.ModuleStorage>()), Times.Never);
    }
}
