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
    private const string BucketName = "modules";
    private const string FinalKey = "ns/name-aws-1.0.0.zip";

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
                ["S3:BucketName"] = BucketName,
                ["S3:Region"] = "eu-west-2",
                ["S3:PresignedUrlExpiryMinutes"] = "11"
            })
            .Build();
    }

    private static ModuleStorage CreateExistingModuleStorage()
    {
        return new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "aws",
            Version = "1.0.0",
            Description = "existing-desc",
            FilePath = FinalKey,
            Dependencies = []
        };
    }

    private static GetObjectMetadataResponse CreateMetadataResponse(
        string @namespace,
        string name,
        string provider,
        string version,
        string description,
        string publishedAt)
    {
        var response = new GetObjectMetadataResponse();
        response.Metadata["namespace"] = @namespace;
        response.Metadata["name"] = name;
        response.Metadata["provider"] = provider;
        response.Metadata["version"] = version;
        response.Metadata["description"] = description;
        response.Metadata["publishedAt"] = publishedAt;
        return response;
    }

    private S3ModuleService CreateService()
    {
        return new S3ModuleService(
            CreateConfiguration(),
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockS3Client.Object);
    }

    private void SetupFinalObjectMissing()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == BucketName &&
                    request.Key == FinalKey),
                default))
            .ThrowsAsync(new AmazonS3Exception("Not found")
            {
                StatusCode = HttpStatusCode.NotFound
            });
    }

    private void SetupFinalObjectExists()
    {
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == BucketName &&
                    request.Key == FinalKey),
                default))
            .ReturnsAsync(new GetObjectMetadataResponse());
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_When_Object_Already_Exists_And_Replace_Is_False()
    {
        SetupFinalObjectExists();

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Adds_Database_Row_With_Correct_Final_Key_And_Finalizes_From_Temp_On_Create_Success()
    {
        SetupFinalObjectMissing();

        PutObjectRequest? putRequest = null;
        CopyObjectRequest? finalizeRequest = null;
        DeleteObjectRequest? deleteRequest = null;
        var operations = new List<string>();

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                putRequest = request;
                operations.Add("put-temp");
            })
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .Callback(() => operations.Add("add-db"))
            .ReturnsAsync(true);

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                finalizeRequest = request;
                operations.Add("copy-finalize");
            })
            .ReturnsAsync(new CopyObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) =>
            {
                deleteRequest = request;
                operations.Add("delete-temp");
            })
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.True(result);
        Assert.NotNull(putRequest);
        Assert.NotEqual(FinalKey, putRequest!.Key);
        Assert.StartsWith(FinalKey, putRequest.Key);
        Assert.Equal("ns", putRequest.Metadata["namespace"]);
        Assert.Equal("name", putRequest.Metadata["name"]);
        Assert.Equal("aws", putRequest.Metadata["provider"]);
        Assert.Equal("1.0.0", putRequest.Metadata["version"]);
        Assert.Equal("desc", putRequest.Metadata["description"]);
        Assert.False(string.IsNullOrWhiteSpace(putRequest.Metadata["publishedAt"]));
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Namespace == "ns" &&
            module.Name == "name" &&
            module.Provider == "aws" &&
            module.Version == "1.0.0" &&
            module.Description == "desc" &&
            module.FilePath == FinalKey &&
            module.Dependencies.Count == 0)), Times.Once);
        Assert.NotNull(finalizeRequest);
        Assert.Equal(BucketName, finalizeRequest!.SourceBucket);
        Assert.Equal(putRequest.Key, finalizeRequest.SourceKey);
        Assert.Equal(BucketName, finalizeRequest.DestinationBucket);
        Assert.Equal(FinalKey, finalizeRequest.DestinationKey);
        Assert.Equal("*", finalizeRequest.IfNoneMatch);
        Assert.NotNull(deleteRequest);
        Assert.Equal(putRequest.Key, deleteRequest!.Key);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        Assert.Equal(["put-temp", "copy-finalize", "add-db", "delete-temp"], operations);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Temp_Key_Only_When_Create_Db_Add_Fails()
    {
        SetupFinalObjectMissing();

        PutObjectRequest? putRequest = null;
        var deleteKeys = new List<string>();
        var finalObjectExists = false;
        Dictionary<string, string>? finalObjectMetadata = null;

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => putRequest = request)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.DestinationKey == FinalKey)
                {
                    finalObjectExists = true;
                    finalObjectMetadata = new Dictionary<string, string>
                    {
                        ["namespace"] = putRequest!.Metadata["namespace"],
                        ["name"] = putRequest.Metadata["name"],
                        ["provider"] = putRequest.Metadata["provider"],
                        ["version"] = putRequest.Metadata["version"],
                        ["description"] = putRequest.Metadata["description"],
                        ["publishedAt"] = putRequest.Metadata["publishedAt"]
                    };
                }
            })
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == BucketName &&
                    request.Key == FinalKey),
                default))
            .Returns<GetObjectMetadataRequest, CancellationToken>((_, _) =>
            {
                if (!finalObjectExists || finalObjectMetadata == null)
                {
                    throw new AmazonS3Exception("Not found")
                    {
                        StatusCode = HttpStatusCode.NotFound
                    };
                }

                return Task.FromResult(CreateMetadataResponse(
                    finalObjectMetadata["namespace"],
                    finalObjectMetadata["name"],
                    finalObjectMetadata["provider"],
                    finalObjectMetadata["version"],
                    finalObjectMetadata["description"],
                    finalObjectMetadata["publishedAt"]));
            });

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) =>
            {
                deleteKeys.Add(request.Key);
                if (request.Key == FinalKey)
                {
                    finalObjectExists = false;
                    finalObjectMetadata = null;
                }
            })
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        Assert.NotNull(putRequest);
        Assert.Contains(putRequest!.Key, deleteKeys);
        Assert.Contains(FinalKey, deleteKeys);
        _mockS3Client.Verify(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Removes_Db_Row_And_Deletes_Temp_Key_When_Create_Finalize_Copy_Fails()
    {
        SetupFinalObjectMissing();

        PutObjectRequest? putRequest = null;
        CopyObjectRequest? copyRequest = null;
        DeleteObjectRequest? deleteRequest = null;

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => putRequest = request)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => copyRequest = request)
            .ThrowsAsync(new AmazonS3Exception("copy failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed
            });

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteRequest = request)
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        Assert.NotNull(putRequest);
        Assert.NotNull(copyRequest);
        Assert.Equal("*", copyRequest!.IfNoneMatch);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        Assert.NotNull(deleteRequest);
        Assert.Equal(putRequest!.Key, deleteRequest!.Key);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Existing_Final_Object_First_And_Finalizes_From_Backup_On_Replace_Success()
    {
        SetupFinalObjectExists();

        var existingModule = CreateExistingModuleStorage();
        var operations = new List<string>();
        string? tempKey = null;
        string? backupKey = null;
        CopyObjectRequest? finalizeRequest = null;

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                tempKey = request.Key;
                operations.Add("put-temp");
            })
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.SourceKey == FinalKey && request.DestinationKey != FinalKey)
                {
                    backupKey = request.DestinationKey;
                    operations.Add("copy-backup");
                }
                else if (request.SourceKey == tempKey && request.DestinationKey == FinalKey)
                {
                    finalizeRequest = request;
                    operations.Add("copy-finalize");
                }
            })
            .ReturnsAsync(new CopyObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.Key == FinalKey)
                {
                    operations.Add("delete-final");
                }
                else if (request.Key == tempKey)
                {
                    operations.Add("delete-temp");
                }
                else if (request.Key == backupKey)
                {
                    operations.Add("delete-backup");
                }
            })
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(existingModule))
            .Callback(() => operations.Add("remove-db"))
            .ReturnsAsync(true);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.Is<ModuleStorage>(module => module.Description == "desc")))
            .Callback(() => operations.Add("add-db"))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(backupKey);
        Assert.NotEqual(FinalKey, tempKey);
        Assert.NotEqual(FinalKey, backupKey);
        Assert.NotEqual(tempKey, backupKey);
        Assert.StartsWith(FinalKey, tempKey!, StringComparison.Ordinal);
        Assert.StartsWith(FinalKey, backupKey!, StringComparison.Ordinal);
        Assert.NotNull(finalizeRequest);
        Assert.Equal("*", finalizeRequest!.IfNoneMatch);
        Assert.Equal(
            ["put-temp", "copy-backup", "delete-final", "remove-db", "copy-finalize", "add-db", "delete-temp", "delete-backup"],
            operations);
    }

    [Fact]
    public async Task UploadModuleAsync_Continues_When_Replace_Remove_Returns_False()
    {
        SetupFinalObjectExists();

        var existingModule = CreateExistingModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ReturnsAsync(new CopyObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(existingModule))
            .ReturnsAsync(false);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
                module.Description == "desc" &&
                module.FilePath == FinalKey)))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(existingModule), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "desc" &&
            module.FilePath == FinalKey)), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == FinalKey),
            default), Times.Once);
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(request =>
                request.SourceKey == FinalKey &&
                request.DestinationKey != FinalKey),
            default), Times.Once);
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(request =>
                request.SourceKey != FinalKey &&
                request.DestinationKey == FinalKey),
            default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Continues_When_Replace_Remove_Throws()
    {
        SetupFinalObjectExists();

        var existingModule = CreateExistingModuleStorage();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ReturnsAsync(new CopyObjectResponse());

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(existingModule))
            .ThrowsAsync(new InvalidOperationException("remove failed"));

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
                module.Description == "desc" &&
                module.FilePath == FinalKey)))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(existingModule), Times.Once);
        _mockDatabaseService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "desc" &&
            module.FilePath == FinalKey)), Times.Once);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(request => request.Key == FinalKey),
            default), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Restores_Old_Final_Object_And_Db_Row_When_Replace_Add_Fails()
    {
        SetupFinalObjectExists();

        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? backupKey = null;
        var deleteKeys = new List<string>();
        CopyObjectRequest? restoreRequest = null;
        PutObjectRequest? putRequest = null;
        var existingPublishedAt = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        var finalObjectExists = true;
        Dictionary<string, string>? finalObjectMetadata = new()
        {
            ["namespace"] = "ns",
            ["name"] = "name",
            ["provider"] = "aws",
            ["version"] = "1.0.0",
            ["description"] = "existing-desc",
            ["publishedAt"] = existingPublishedAt
        };

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                tempKey = request.Key;
                putRequest = request;
            })
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.SourceKey == FinalKey && request.DestinationKey != FinalKey)
                {
                    backupKey = request.DestinationKey;
                }
                else if (request.SourceKey == tempKey && request.DestinationKey == FinalKey)
                {
                    finalObjectExists = true;
                    finalObjectMetadata = new Dictionary<string, string>
                    {
                        ["namespace"] = putRequest!.Metadata["namespace"],
                        ["name"] = putRequest.Metadata["name"],
                        ["provider"] = putRequest.Metadata["provider"],
                        ["version"] = putRequest.Metadata["version"],
                        ["description"] = putRequest.Metadata["description"],
                        ["publishedAt"] = putRequest.Metadata["publishedAt"]
                    };
                }
                else if (request.DestinationKey == FinalKey)
                {
                    restoreRequest = request;
                    finalObjectExists = true;
                    finalObjectMetadata = new Dictionary<string, string>
                    {
                        ["namespace"] = "ns",
                        ["name"] = "name",
                        ["provider"] = "aws",
                        ["version"] = "1.0.0",
                        ["description"] = "existing-desc",
                        ["publishedAt"] = existingPublishedAt
                    };
                }
            })
            .ReturnsAsync(new CopyObjectResponse());

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == BucketName &&
                    request.Key == FinalKey),
                default))
            .Returns<GetObjectMetadataRequest, CancellationToken>((_, _) =>
            {
                if (!finalObjectExists || finalObjectMetadata == null)
                {
                    throw new AmazonS3Exception("Not found")
                    {
                        StatusCode = HttpStatusCode.NotFound
                    };
                }

                return Task.FromResult(CreateMetadataResponse(
                    finalObjectMetadata["namespace"],
                    finalObjectMetadata["name"],
                    finalObjectMetadata["provider"],
                    finalObjectMetadata["version"],
                    finalObjectMetadata["description"],
                    finalObjectMetadata["publishedAt"]));
            });

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) =>
            {
                deleteKeys.Add(request.Key);
                if (request.Key == FinalKey)
                {
                    finalObjectExists = false;
                    finalObjectMetadata = null;
                }
            })
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(existingModule))
            .ReturnsAsync(true);

        _mockDatabaseService
            .SetupSequence(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(backupKey);
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(request =>
                request.SourceKey == FinalKey &&
                request.DestinationKey == backupKey),
            default), Times.Once);
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(request =>
                request.SourceKey == backupKey &&
                request.DestinationKey == FinalKey),
            default), Times.Once);
        Assert.NotNull(restoreRequest);
        Assert.Equal("*", restoreRequest!.IfNoneMatch);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "desc" &&
            module.FilePath == FinalKey)), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "existing-desc" &&
            module.FilePath == FinalKey)), Times.Once);
        Assert.Contains(FinalKey, deleteKeys);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(backupKey!, deleteKeys);
    }

    [Fact]
    public async Task UploadModuleAsync_Restores_Old_Final_Object_And_Db_Row_When_Replace_Finalize_Copy_Fails()
    {
        SetupFinalObjectExists();

        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? backupKey = null;
        var deleteKeys = new List<string>();
        CopyObjectRequest? finalizeRequest = null;
        CopyObjectRequest? restoreRequest = null;
        var finalObjectExists = true;

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.SourceKey == FinalKey && request.DestinationKey != FinalKey)
                {
                    backupKey = request.DestinationKey;
                }
                else if (request.SourceKey == tempKey && request.DestinationKey == FinalKey)
                {
                    finalizeRequest = request;
                }
                else if (request.SourceKey == backupKey && request.DestinationKey == FinalKey)
                {
                    restoreRequest = request;
                    finalObjectExists = true;
                }
            })
            .Returns<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                if (request.SourceKey == tempKey && request.DestinationKey == FinalKey)
                {
                    throw new AmazonS3Exception("copy failed")
                    {
                        StatusCode = HttpStatusCode.InternalServerError
                    };
                }

                return Task.FromResult(new CopyObjectResponse());
            });

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == BucketName &&
                    request.Key == FinalKey),
                default))
            .Returns<GetObjectMetadataRequest, CancellationToken>((_, _) =>
            {
                if (!finalObjectExists)
                {
                    throw new AmazonS3Exception("Not found")
                    {
                        StatusCode = HttpStatusCode.NotFound
                    };
                }

                return Task.FromResult(CreateMetadataResponse(
                    "ns",
                    "name",
                    "aws",
                    "1.0.0",
                    "existing-desc",
                    existingModule.PublishedAt.ToString("o")));
            });

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        _mockDatabaseService
            .Setup(x => x.RemoveModuleExactAsync(existingModule))
            .ReturnsAsync(true);

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(backupKey);
        Assert.NotNull(finalizeRequest);
        Assert.Equal("*", finalizeRequest!.IfNoneMatch);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.Is<ModuleStorage>(module =>
            module.Description == "existing-desc" &&
            module.FilePath == FinalKey)), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "existing-desc" &&
            module.FilePath == FinalKey)), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "desc" &&
            module.FilePath == FinalKey)), Times.Never);
        Assert.NotNull(restoreRequest);
        Assert.Equal("*", restoreRequest!.IfNoneMatch);
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(request =>
                request.SourceKey == backupKey &&
                request.DestinationKey == FinalKey),
            default), Times.Once);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(backupKey!, deleteKeys);
    }

    [Fact]
    public async Task UploadModuleAsync_Logs_When_Temp_Key_Cleanup_Delete_Fails()
    {
        SetupFinalObjectMissing();

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("delete failed")
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("temporary S3 object")),
                It.IsAny<AmazonS3Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
