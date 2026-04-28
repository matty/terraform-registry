using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.UnitTests.S3;

public class S3ModuleServicePurgeAndHealthTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServicePurgeAndHealthTests()
    {
        _mockS3Client
            .SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response())
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private S3ModuleService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S3:BucketName"] = "modules",
                ["S3:Region"] = "eu-west-2"
            })
            .Build();

        return new S3ModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object, _mockS3Client.Object);
    }

    [Fact]
    public async Task PurgeModuleVersionAsync_Returns_False_When_Module_Row_Is_Missing()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);

        var service = CreateService();

        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
    }

    [Fact]
    public async Task PurgeModuleVersionAsync_Deletes_Database_Row_And_Object()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip",
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.True(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
    }

    [Fact]
    public async Task CheckStorageAsync_Returns_Healthy_When_List_Succeeds()
    {
        var service = CreateService();

        var result = await service.CheckStorageAsync();

        Assert.Equal((true, (string?)null), result);
    }

    [Fact]
    public async Task CheckStorageAsync_Returns_Unhealthy_When_List_Fails()
    {
        _mockS3Client
            .SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response())
            .ThrowsAsync(new AmazonS3Exception("bucket unavailable"));

        var service = CreateService();
        var (healthy, reason) = await service.CheckStorageAsync();

        Assert.False(healthy);
        Assert.Contains("S3 storage unreachable", reason);
    }
}
