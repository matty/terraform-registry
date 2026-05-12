using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.UnitTests.S3;

public class S3ModuleServiceDownloadTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServiceDownloadTests()
    {
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["S3:BucketName"] = "modules",
                ["S3:Region"] = "eu-west-2",
                ["S3:PresignedUrlExpiryMinutes"] = "11"
            })
            .Build();
    }

    private static ModuleStorage CreateModuleStorage()
    {
        return new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip",
            Dependencies = []
        };
    }

    private S3ModuleService CreateService()
    {
        return new S3ModuleService(
            CreateConfiguration(),
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockS3Client.Object);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullWhenModuleIsNotInDatabase()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);

        var service = CreateService();

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullWhenObjectIsMissingInS3()
    {
        var moduleStorage = CreateModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        var service = CreateService();

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
        _mockS3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsAPresignedGetUrlWhenModuleAndObjectExist()
    {
        const string expectedUrl = "https://example.invalid/presigned";
        var moduleStorage = CreateModuleStorage();
        GetPreSignedUrlRequest? capturedRequest = null;

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Callback<GetPreSignedUrlRequest>(request => capturedRequest = request)
            .Returns(expectedUrl);

        var service = CreateService();
        var beforeCall = DateTime.UtcNow;

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0");
        var afterCall = DateTime.UtcNow;

        Assert.Equal(expectedUrl, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("modules", capturedRequest!.BucketName);
        Assert.Equal(moduleStorage.FilePath, capturedRequest.Key);
        Assert.Equal(HttpVerb.GET, capturedRequest.Verb);
        Assert.NotNull(capturedRequest.Expires);
        Assert.InRange(
            capturedRequest.Expires!.Value,
            beforeCall.AddMinutes(11),
            afterCall.AddMinutes(11));
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
        _mockS3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Once);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullWhenPresigningThrows()
    {
        var moduleStorage = CreateModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Throws(new InvalidOperationException("signing failed"));

        var service = CreateService();

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
        _mockS3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("pre-signed URL")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullAndLogsErrorWhenMetadataLookupThrowsNon404()
    {
        var moduleStorage = CreateModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ThrowsAsync(new AmazonS3Exception("boom")
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var service = CreateService();

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
        _mockS3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("Error checking S3 object")),
                It.IsAny<AmazonS3Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task OpenModulePackageStreamAsyncReturnsNullWhenModuleIsNotInDatabase()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);

        var service = CreateService();

        var result = await service.OpenModulePackageStreamAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockDatabaseService.Verify(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockS3Client.Verify(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task OpenModulePackageStreamAsyncReturnsNullWhenObjectIsMissingInS3()
    {
        var moduleStorage = CreateModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        var service = CreateService();

        var result = await service.OpenModulePackageStreamAsync("ns", "name", "aws", "1.0.0");

        Assert.Null(result);
        _mockS3Client.Verify(x => x.GetObjectAsync(
            It.Is<GetObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
    }

    [Fact]
    public async Task OpenModulePackageStreamAsyncReturnsObjectStreamWhenModuleAndObjectExist()
    {
        var moduleStorage = CreateModuleStorage();
        await using var objectStream = new MemoryStream([1, 2, 3]);

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(moduleStorage);

        _mockS3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == moduleStorage.FilePath),
                default))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = objectStream
            });

        var service = CreateService();

        await using var result = await service.OpenModulePackageStreamAsync("ns", "name", "aws", "1.0.0");

        Assert.Same(objectStream, result);
        _mockS3Client.Verify(x => x.GetObjectAsync(
            It.Is<GetObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == moduleStorage.FilePath),
            default), Times.Once);
    }
}
