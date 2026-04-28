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
    public void Constructor_ThrowsArgumentNullException_When_BucketName_Is_Missing()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["S3:Region"] = "eu-west-2"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("S3:BucketName", ex.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_When_Region_Is_Missing()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
        {
            ["S3:BucketName"] = "modules"
        });

        var ex = Assert.Throws<ArgumentNullException>(() =>
            new S3ModuleService(
                config,
                _mockDatabaseService.Object,
                _mockLogger.Object,
                _mockS3Client.Object));

        Assert.Equal("S3:Region", ex.ParamName);
    }

    [Fact]
    public void Constructor_Uses_Provided_IAmazonS3_And_Skips_Factory()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
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
            x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.BucketName == "modules" && r.MaxKeys == 1),
                default),
            Times.Once);
    }

    [Fact]
    public void Constructor_Uses_Explicit_Credentials_And_Endpoint_Settings_When_Configured()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
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
    public void Constructor_Uses_Default_Credential_Chain_When_Explicit_Credentials_Are_Absent()
    {
        var config = CreateConfiguration(new Dictionary<string, string?>
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
}
