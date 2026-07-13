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
    private readonly Mock<IDatabaseService> _database = new();
    private readonly Mock<ILogger<S3ModuleService>> _logger = new();
    private readonly Mock<IAmazonS3> _s3 = new();

    public S3ModuleServiceUploadTests()
    {
        _logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _database.Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0"))
            .ReturnsAsync(value: null);
        _database.Setup(x => x.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Returns(Task.CompletedTask);
        _database.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), It.IsAny<ModuleStorage?>()))
            .ReturnsAsync(true);
        _s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());
        _s3.Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ReturnsAsync(new CopyObjectResponse());
        _s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());
    }

    private S3ModuleService CreateService() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["S3:BucketName"] = "modules",
            ["S3:Region"] = "eu-west-2"
        }).Build(), _database.Object, _logger.Object, _s3.Object);

    private static ModuleStorage Existing(string filePath = "ns/name-aws-1.0.0.winner.zip") => new()
    {
        Namespace = "ns",
        Name = "name",
        Provider = "aws",
        Version = "1.0.0",
        Description = "old",
        FilePath = filePath,
        PublishedAt = DateTime.UtcNow.AddMinutes(-1),
        Dependencies = [],
        Metadata = new ModuleArtifactMetadata { RootSubdirectory = "old" }
    };

    [Fact]
    public async Task UploadModuleAsyncRejectsExistingCatalogCoordinateWithoutStaging()
    {
        _database.Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(Existing());
        using var content = new MemoryStream([1]);

        var result = await CreateService().UploadModuleAsync("ns", "name", "aws", "1.0.0", content, "desc");

        Assert.False(result);
        _database.Verify(x => x.CreatePublicationAttemptWithExtractionJobAsync(
            It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()), Times.Never);
        _s3.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncStagesAndCommitsAttemptOwnedObjects()
    {
        PutObjectRequest? stagedPut = null;
        CopyObjectRequest? promotion = null;
        ModulePublicationAttempt? attempt = null;
        ModuleStorage? committed = null;
        _s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => stagedPut = request)
            .ReturnsAsync(new PutObjectResponse());
        _s3.Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => promotion = request)
            .ReturnsAsync(new CopyObjectResponse());
        _database.Setup(x => x.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Callback<ModulePublicationAttempt, ModuleExtractionJob>((value, _) => attempt = value)
            .Returns(Task.CompletedTask);
        _database.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .Callback<ModulePublicationAttempt, ModuleStorage, ModuleStorage?>((_, module, _) => committed = module)
            .ReturnsAsync(true);
        using var content = new MemoryStream([1]);

        var result = await CreateService().UploadModuleAsync("ns", "name", "aws", "1.0.0", content, "desc");

        Assert.True(result);
        Assert.NotNull(attempt);
        Assert.NotNull(stagedPut);
        Assert.NotNull(promotion);
        Assert.NotNull(committed);
        Assert.Equal(ModulePublicationAttemptState.Staged, attempt!.State);
        Assert.Equal(stagedPut!.Key, attempt.StagingKey);
        Assert.Equal(stagedPut.Key, promotion!.SourceKey);
        Assert.Equal(promotion.DestinationKey, committed!.FilePath);
        _database.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        _database.Verify(x => x.ReplaceModuleExactAsync(It.IsAny<ModuleStorage>(), It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncCleansOnlyAttemptObjectsWhenCatalogCommitLoses()
    {
        var deleted = new List<string>();
        var winner = Existing();
        _database.Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(winner);
        _database.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), winner))
            .ReturnsAsync(false);
        _database.Setup(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        _s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleted.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());
        using var content = new MemoryStream([1]);

        var result = await CreateService().UploadModuleAsync("ns", "name", "aws", "1.0.0", content, "desc", replace: true);

        Assert.False(result);
        Assert.DoesNotContain(winner.FilePath, deleted);
        Assert.Equal(2, deleted.Count);
        _database.Verify(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UploadModuleAsyncCommitsReplacementMetadataAndDeletesSupersededObjectAfterCas()
    {
        var winner = Existing();
        var replacementMetadata = new ModuleArtifactMetadata { RootSubdirectory = "replacement" };
        var deleted = new List<string>();
        _database.Setup(x => x.GetModuleStorageAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(winner);
        _database.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(),
                It.Is<ModuleStorage>(module => module.Metadata == replacementMetadata && module.Description == "new"),
                winner)).ReturnsAsync(true);
        _s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleted.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());
        using var content = new MemoryStream([1]);

        var result = await CreateService().UploadModuleAsync("ns", "name", "aws", "1.0.0", content, "new",
            replace: true, metadata: replacementMetadata);

        Assert.True(result);
        Assert.Contains(winner.FilePath, deleted);
        _database.Verify(x => x.ReplaceModuleExactAsync(It.IsAny<ModuleStorage>(), It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncMarksAttemptFailedWhenPromotionFails()
    {
        _s3.Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("copy failed"));
        _database.Setup(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        using var content = new MemoryStream([1]);

        var result = await CreateService().UploadModuleAsync("ns", "name", "aws", "1.0.0", content, "desc");

        Assert.False(result);
        _database.Verify(x => x.TryCommitStagedPublicationAsync(
            It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), It.IsAny<ModuleStorage?>()), Times.Never);
        _database.Verify(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Once);
    }
}
