using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.UnitTests.S3;

public class S3ModuleServiceConstructorTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<IS3ClientFactory> _mockClientFactory = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServiceConstructorTests()
    {
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> settings)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionWhenBucketNameIsMissing()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:Region"] = "eu-west-2"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("S3:BucketName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionWhenBucketNameIsBlank()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "   ",
            ["S3:Region"] = "eu-west-2"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("S3:BucketName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionWhenRegionIsMissing()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("S3:Region", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionWhenRegionIsBlank()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "\t"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("S3:Region", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializationUsesProvidedIAmazonS3AndSkipsFactory()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "eu-west-2"
        });

        var service = new S3ModuleService(
            config,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockS3Client.Object,
            _mockClientFactory.Object);

        Assert.NotNull(service);
        _mockClientFactory.Verify(
            x => x.Create(It.IsAny<AmazonS3Config>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        _mockS3Client.Verify(
            x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default),
            Times.Never);

        await service.InitializeStorageAsync(CancellationToken.None);

        _mockS3Client.Verify(
            x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.BucketName == "modules" && r.MaxKeys == 1),
                default),
            Times.Once);
    }

    [Fact]
    public void ConstructorUsesExplicitCredentialsAndEndpointSettingsWhenConfigured()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "eu-west-2",
            ["S3:ServiceUrl"] = "http://minio:9000",
            ["S3:ForcePathStyle"] = "true",
            ["S3:AccessKeyId"] = "test-key",
            ["S3:SecretAccessKey"] = "test-secret",
            ["S3:SessionToken"] = "test-session"
        });

        _mockClientFactory
            .Setup(x => x.Create(It.IsAny<AmazonS3Config>(), "test-key", "test-secret", "test-session"))
            .Callback<AmazonS3Config, string?, string?, string?>((cfg, _, _, _) =>
            {
                Assert.Equal("http://minio:9000", new Uri(cfg.ServiceURL).GetLeftPart(UriPartial.Authority));
                Assert.True(cfg.ForcePathStyle);
                Assert.Equal("eu-west-2", cfg.AuthenticationRegion);
            })
            .Returns(_mockS3Client.Object);

        var service = new S3ModuleService(
            config,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            null,
            _mockClientFactory.Object);

        Assert.NotNull(service);
        _mockClientFactory.Verify(
            x => x.Create(It.IsAny<AmazonS3Config>(), "test-key", "test-secret", "test-session"),
            Times.Once);
    }

    [Fact]
    public void ConstructorUsesDefaultCredentialChainWhenExplicitCredentialsAreAbsent()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "eu-west-2"
        });

        _mockClientFactory
            .Setup(x => x.Create(It.IsAny<AmazonS3Config>(), null, null, null))
            .Returns(_mockS3Client.Object);

        var service = new S3ModuleService(
            config,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            null,
            _mockClientFactory.Object);

        Assert.NotNull(service);
        _mockClientFactory.Verify(
            x => x.Create(It.IsAny<AmazonS3Config>(), null, null, null),
            Times.Once);
    }

    [Fact]
    public void ConstructorInvalidPresignedUrlExpiryMinutesDefaultsAndDoesNotThrow()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "eu-west-2",
            ["S3:PresignedUrlExpiryMinutes"] = "abc"
        });

        _mockClientFactory
            .Setup(x => x.Create(It.IsAny<AmazonS3Config>(), null, null, null))
            .Returns(_mockS3Client.Object);

        var service = new S3ModuleService(
            config,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            null,
            _mockClientFactory.Object);

        Assert.NotNull(service);
        _mockClientFactory.Verify(
            x => x.Create(It.IsAny<AmazonS3Config>(), null, null, null),
            Times.Once);
    }
}
