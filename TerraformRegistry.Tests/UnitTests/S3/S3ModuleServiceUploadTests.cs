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
    private const string LogicalKey = "ns/name-aws-1.0.0.zip";

    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServiceUploadTests()
    {
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response());

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);
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
            FilePath = $"{LogicalKey}.existing",
            PublishedAt = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
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
        var response = new GetObjectMetadataResponse
        {
            ETag = "\"etag\""
        };
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

    [Fact]
    public async Task UploadModuleAsync_Returns_False_When_Database_Row_Already_Exists_And_Replace_Is_False()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(CreateExistingModuleStorage());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Adds_Database_Row_With_Unique_Final_Key_And_Finalizes_From_Temp_On_Create_Success()
    {
        PutObjectRequest? putRequest = null;
        CopyObjectRequest? finalizeRequest = null;
        DeleteObjectRequest? deleteRequest = null;
        ModuleStorage? addedModule = null;
        var operations = new List<string>();

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                putRequest = request;
                operations.Add("put-temp");
            })
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                finalizeRequest = request;
                operations.Add("copy-finalize");
            })
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .Callback<ModuleStorage>(module =>
            {
                addedModule = module;
                operations.Add("add-db");
            })
            .ReturnsAsync(true);

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
        Assert.NotNull(finalizeRequest);
        Assert.NotNull(addedModule);
        Assert.NotNull(deleteRequest);
        Assert.NotEqual(LogicalKey, finalizeRequest!.DestinationKey);
        Assert.StartsWith(LogicalKey, finalizeRequest.DestinationKey, StringComparison.Ordinal);
        Assert.Equal(putRequest!.Key, finalizeRequest.SourceKey);
        Assert.Equal(finalizeRequest.DestinationKey, addedModule!.FilePath);
        Assert.Equal(putRequest.Key, deleteRequest!.Key);
        Assert.Equal("ns", putRequest.Metadata["namespace"]);
        Assert.Equal("name", putRequest.Metadata["name"]);
        Assert.Equal("aws", putRequest.Metadata["provider"]);
        Assert.Equal("1.0.0", putRequest.Metadata["version"]);
        Assert.Equal("desc", putRequest.Metadata["description"]);
        Assert.False(string.IsNullOrWhiteSpace(putRequest.Metadata["publishedAt"]));
        Assert.Equal(["put-temp", "copy-finalize", "add-db", "delete-temp"], operations);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Deletes_Unique_Final_Object_When_Create_Db_Add_Fails()
    {
        PutObjectRequest? putRequest = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => putRequest = request)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        Assert.NotNull(putRequest);
        Assert.NotNull(finalKey);
        Assert.Contains(putRequest!.Key, deleteKeys);
        Assert.Contains(finalKey!, deleteKeys);
        _mockS3Client.Verify(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Allows_Replace_When_Current_Database_Row_Is_Missing()
    {
        ModuleStorage? addedModule = null;

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .Callback<ModuleStorage>(module => addedModule = module)
            .ReturnsAsync(true);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        Assert.NotNull(addedModule);
        Assert.StartsWith(LogicalKey, addedModule!.FilePath, StringComparison.Ordinal);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_When_Module_Row_Exists_In_Trash()
    {
        _mockDatabaseService
            .Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(CreateExistingModuleStorage());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Never);
        _mockS3Client.Verify(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(It.IsAny<ModuleStorage>(), It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Removes_Db_Row_And_Deletes_Temp_Key_When_Create_Finalize_Copy_Fails()
    {
        PutObjectRequest? putRequest = null;
        CopyObjectRequest? copyRequest = null;
        var deleteKeys = new List<string>();

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
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.False(result);
        Assert.NotNull(putRequest);
        Assert.NotNull(copyRequest);
        Assert.Equal("*", copyRequest!.IfNoneMatch);
        Assert.Contains(putRequest!.Key, deleteKeys);
        Assert.Contains(copyRequest.DestinationKey, deleteKeys);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.RemoveModuleExactAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Continues_When_Create_Finalize_Copy_Throws_But_Final_Metadata_Matches_Upload()
    {
        PutObjectRequest? putRequest = null;
        string? finalKey = null;
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
                finalKey = request.DestinationKey;
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
            })
            .ThrowsAsync(new AmazonS3Exception("copy failed")
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), default))
            .Returns<GetObjectMetadataRequest, CancellationToken>((request, _) =>
            {
                if (!finalObjectExists || finalObjectMetadata == null || request.Key != finalKey)
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

        _mockDatabaseService
            .Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()))
            .ReturnsAsync(true);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc");

        Assert.True(result);
        Assert.NotNull(finalKey);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Description == "desc" &&
            module.FilePath == finalKey)), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsync_Replaces_With_New_Unique_Final_Key_And_Deletes_Previous_Object()
    {
        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.ReplaceModuleExactAsync(
                existingModule,
                It.Is<ModuleStorage>(module =>
                    module.Description == "desc" &&
                    module.FilePath == finalKey)))
            .ReturnsAsync(true);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.True(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(finalKey);
        Assert.NotEqual(existingModule.FilePath, finalKey);
        Assert.StartsWith(LogicalKey, finalKey!, StringComparison.Ordinal);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(existingModule.FilePath, deleteKeys);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(
            existingModule,
            It.Is<ModuleStorage>(module =>
                module.Description == "desc" &&
                module.FilePath == finalKey)), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_And_Cleans_Up_When_Replace_Update_Returns_False()
    {
        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(finalKey);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(finalKey!, deleteKeys);
        Assert.DoesNotContain(existingModule.FilePath, deleteKeys);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_And_Cleans_Up_When_Replace_Update_Throws()
    {
        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()))
            .ThrowsAsync(new InvalidOperationException("replace failed"));

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(finalKey);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(finalKey!, deleteKeys);
        Assert.DoesNotContain(existingModule.FilePath, deleteKeys);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_And_Deletes_Unique_Final_Object_When_Replace_Finalize_Copy_Fails()
    {
        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ThrowsAsync(new AmazonS3Exception("copy failed")
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(finalKey);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(finalKey!, deleteKeys);
        Assert.DoesNotContain(existingModule.FilePath, deleteKeys);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()), Times.Never);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Returns_False_And_Deletes_Unique_Final_Object_When_Replace_Update_Fails_After_Finalize()
    {
        var existingModule = CreateExistingModuleStorage();
        string? tempKey = null;
        string? finalKey = null;
        var deleteKeys = new List<string>();

        _mockDatabaseService
            .Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(existingModule);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => tempKey = request.Key)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => finalKey = request.DestinationKey)
            .ReturnsAsync(new CopyObjectResponse());

        _mockDatabaseService
            .Setup(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()))
            .ReturnsAsync(false);

        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteKeys.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.UploadModuleAsync("ns", "name", "aws", "1.0.0", stream, "desc", replace: true);

        Assert.False(result);
        Assert.NotNull(tempKey);
        Assert.NotNull(finalKey);
        Assert.Contains(tempKey!, deleteKeys);
        Assert.Contains(finalKey!, deleteKeys);
        Assert.DoesNotContain(existingModule.FilePath, deleteKeys);
        _mockDatabaseService.Verify(x => x.ReplaceModuleExactAsync(existingModule, It.IsAny<ModuleStorage>()), Times.Once);
        _mockDatabaseService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsync_Logs_When_Temp_Key_Cleanup_Delete_Fails()
    {
        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ReturnsAsync(new CopyObjectResponse());

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
