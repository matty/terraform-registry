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

public class S3ModuleServicePurgeAndHealthTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServicePurgeAndHealthTests()
    {
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockS3Client
            .SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response())
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private S3ModuleService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["S3:BucketName"] = "modules",
                ["S3:Region"] = "eu-west-2"
            })
            .Build();

        return new S3ModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object, _mockS3Client.Object);
    }

    private static GetObjectMetadataResponse CreateMetadataResponse(ModuleStorage module, DateTime? publishedAt = null)
    {
        var response = new GetObjectMetadataResponse();
        response.Metadata["namespace"] = module.Namespace;
        response.Metadata["name"] = module.Name;
        response.Metadata["provider"] = module.Provider;
        response.Metadata["version"] = module.Version;
        response.Metadata["description"] = module.Description;
        response.Metadata["publishedAt"] = (publishedAt ?? module.PublishedAt).ToString("o");
        return response;
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncReturnsFalseWhenModuleRowIsMissing()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);

        var service = CreateService();

        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncRestoresCatalogRowWhenObjectDeletionIsCancelled()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns", Name = "name", Provider = "aws", Version = "1.0.0", Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip", PublishedAt = DateTime.UtcNow, Dependencies = []
        };
        using var cancellation = new CancellationTokenSource();
        _mockDatabaseService.Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0", cancellation.Token))
            .ReturnsAsync(module);
        _mockDatabaseService.Setup(x => x.RemoveModuleExactAsync(module, cancellation.Token)).ReturnsAsync(true);
        _mockDatabaseService.Setup(x => x.AddModuleAsync(module, CancellationToken.None)).ReturnsAsync(true);
        _mockS3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), cancellation.Token))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = [new S3Object { Key = module.FilePath }] });
        _mockS3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), cancellation.Token))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateService().PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0", cancellation.Token));

        _mockDatabaseService.Verify(x => x.AddModuleAsync(module, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncDeletesDatabaseRowAndObject()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };
        var operations = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .Callback(() => operations.Add("remove-active-row"))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request =>
                    request.BucketName == "modules" &&
                    request.Prefix == "ns/name-aws-1.0.0.zip"),
                default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = "ns/name-aws-1.0.0.zip.current" },
                    new S3Object { Key = "ns/name-aws-1.0.0.zip.previous" }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback(() => operations.Add("delete-object"))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.True(result);
        Assert.Equal(["remove-active-row", "delete-object", "delete-object"], operations);
        _mockS3Client.Verify(x => x.ListObjectsV2Async(
            It.Is<ListObjectsV2Request>(request =>
                request.BucketName == "modules" &&
                request.Prefix == "ns/name-aws-1.0.0.zip"),
            default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip.current"),
            default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip.previous"),
            default), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(module), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncDeletesAllMatchingObjectsAcrossPages()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .Returns<ListObjectsV2Request, CancellationToken>((request, _) =>
            {
                if (request.BucketName != "modules" || request.Prefix != "ns/name-aws-1.0.0.zip")
                {
                    return Task.FromResult(new ListObjectsV2Response());
                }

                return request.ContinuationToken switch
                {
                    null => Task.FromResult(new ListObjectsV2Response
                    {
                        IsTruncated = true,
                        NextContinuationToken = "next",
                        S3Objects =
                        [
                            new S3Object { Key = "ns/name-aws-1.0.0.zip.current" }
                        ]
                    }),
                    "next" => Task.FromResult(new ListObjectsV2Response
                    {
                        IsTruncated = false,
                        S3Objects =
                        [
                            new S3Object { Key = "ns/name-aws-1.0.0.zip.previous" }
                        ]
                    }),
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected continuation token: {request.ContinuationToken}")
                };
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.True(result);
        _mockS3Client.Verify(x => x.ListObjectsV2Async(
            It.Is<ListObjectsV2Request>(request =>
                request.BucketName == "modules" &&
                request.Prefix == "ns/name-aws-1.0.0.zip" &&
                request.ContinuationToken == null),
            default), Times.Once);
        _mockS3Client.Verify(x => x.ListObjectsV2Async(
            It.Is<ListObjectsV2Request>(request =>
                request.BucketName == "modules" &&
                request.Prefix == "ns/name-aws-1.0.0.zip" &&
                request.ContinuationToken == "next"),
            default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip.current"),
            default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request =>
                request.BucketName == "modules" &&
                request.Key == "ns/name-aws-1.0.0.zip.previous"),
            default), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(module), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncReturnsFalseWhenS3DeleteFailsAndKeepsRowForRetry()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .ReturnsAsync(true);
        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = "ns/name-aws-1.0.0.zip.current" }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("delete failed"));

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(module), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(module), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncReturnsFalseWhenMetadataReadFailsAndKeepsRowForRetry()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("metadata failed")
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncReturnsFalseWhenPublishedAtMetadataIsInvalidAndKeepsRowForRetry()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };
        var response = CreateMetadataResponse(module);
        response.Metadata["publishedAt"] = "not-a-date";

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(response);

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveDeletedModuleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncSkipsNewerObjectsAndFailsWhenCurrentRowChanges()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };
        const string newerKey = "ns/name-aws-1.0.0.zip.newer";

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .ReturnsAsync(false);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath },
                    new S3Object { Key = newerKey }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == module.FilePath),
                default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == newerKey),
                default))
            .ReturnsAsync(CreateMetadataResponse(module, module.PublishedAt.AddMinutes(5)));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == module.FilePath),
            default), Times.Never);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == newerKey),
            default), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(module), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncRestoresActiveRowWhenS3DeleteFailsAfterRowRemove()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .ReturnsAsync(true);
        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("delete failed"));

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(module), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(module), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncRestoresActiveRowWithoutDeletingCurrentObjectWhenHistoricalDeleteFails()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };
        const string previousKey = "ns/name-aws-1.0.0.zip.previous";

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(module))
            .ReturnsAsync(true);
        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath },
                    new S3Object { Key = previousKey }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == module.FilePath),
                default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == previousKey),
                default))
            .ReturnsAsync(CreateMetadataResponse(module, module.PublishedAt.AddMinutes(-1)));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == previousKey),
                default))
            .ThrowsAsync(new AmazonS3Exception("delete failed"));

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == previousKey),
            default), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == module.FilePath),
            default), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(module), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncDeletesSoftDeletedRowBeforeObjectCleanup()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };
        var operations = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"))
            .Callback(() => operations.Add("remove-deleted-row"))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback(() => operations.Add("delete-object"))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.True(result);
        Assert.Equal(["remove-deleted-row", "delete-object"], operations);
        _mockDatabaseService.Verify(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncReturnsFalseWhenSoftDeletedRowWasRestoredBeforeRemove()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(false);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncRestoresSoftDeletedRowWhenS3DeleteFailsAfterRowRemove()
    {
        var module = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "ns/name-aws-1.0.0.zip.current",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            Dependencies = []
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(module);
        _mockDatabaseService
            .Setup(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(true);
        _mockDatabaseService
            .Setup(x => x.AddDeletedModuleAsync(module))
            .ReturnsAsync(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = module.FilePath }
                ]
            });
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .ReturnsAsync(CreateMetadataResponse(module));
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("delete failed"));

        var service = CreateService();
        var result = await service.PurgeModuleVersionAsync("ns", "name", "aws", "1.0.0");

        Assert.False(result);
        _mockDatabaseService.Verify(x => x.RemoveDeletedModuleAsync("ns", "name", "aws", "1.0.0"), Times.Once);
        _mockDatabaseService.Verify(x => x.AddDeletedModuleAsync(module), Times.Once);
    }

    [Fact]
    public async Task CheckStorageAsyncReturnsHealthyWhenListSucceeds()
    {
        var service = CreateService();

        var result = await service.CheckStorageAsync();

        Assert.True(result.Healthy);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task CheckStorageAsyncReturnsUnhealthyWhenListFails()
    {
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ThrowsAsync(new AmazonS3Exception("bucket unavailable"));

        var service = CreateService();
        var (healthy, reason) = await service.CheckStorageAsync();

        Assert.False(healthy);
        Assert.Contains("S3 storage unreachable", reason);
    }
}
