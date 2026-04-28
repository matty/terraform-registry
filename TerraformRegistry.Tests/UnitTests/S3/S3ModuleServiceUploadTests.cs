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

public class S3ModuleServiceUploadTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServiceUploadTests()
    {
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S3:BucketName"] = "modules",
                ["S3:Region"] = "eu-west-2",
                ["S3:PresignedUrlExpiryMinutes"] = "11"
            })
            .Build();
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
    public async Task UploadModuleAsync_Returns_False_When_Object_Already_Exists_And_Replace_Is_False()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
            It.Is<GetObjectMetadataRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Old_Object_When_Replace_Is_True()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.Is<ModuleStorage>(module =>
            module.Namespace == "ns" &&
            module.Name == "name" &&
            module.Provider == "aws" &&
            module.Version == "1.0.0" &&
            module.FilePath == "ns/name-aws-1.0.0.zip")), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Adds_Database_Row_With_Correct_Object_Key_When_Upload_Succeeds()
    {
        PutObjectRequest? putRequest = null;

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => putRequest = request)
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.True(result);
        Assert.NotNull(putRequest);
        Assert.Equal("modules", putRequest!.BucketName);
        Assert.Equal("ns/name-aws-1.0.0.zip", putRequest.Key);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Namespace == "ns" &&
            module.Name == "name" &&
            module.Provider == "aws" &&
            module.Version == "1.0.0" &&
            module.Description == "desc" &&
            module.FilePath == "ns/name-aws-1.0.0.zip" &&
            module.Dependencies.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Just_Uploaded_Object_When_Database_Add_Fails()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Just_Uploaded_Object_When_Database_Add_Throws()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ThrowsAsync(new InvalidOperationException("db failed"));

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_Without_Db_Add_Or_Cleanup_When_Conditional_Create_Conflicts()
    {
        PutObjectRequest? putRequest = null;

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => putRequest = request)
            .ThrowsAsync(new AmazonS3Exception("conflict")
            {
                StatusCode = HttpStatusCode.PreconditionFailed
            });

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        Assert.NotNull(putRequest);
        Assert.Equal("*", putRequest!.IfNoneMatch);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Does_Not_Delete_Final_Key_When_Replace_Db_Add_Fails()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Does_Not_Delete_Final_Key_When_Replace_Db_Add_Throws()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request =>
                    request.BucketName == "modules" &&
                    request.Key == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ThrowsAsync(new InvalidOperationException("db failed"));

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
    }
}
