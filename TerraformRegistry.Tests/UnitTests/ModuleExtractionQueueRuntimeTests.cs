using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleExtractionQueueRuntimeTests
{
    [Fact]
    public async Task QueueAsyncReturnsFalseAndDoesNotQueueWhenExtractionDisabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = CreateService(config.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.False(queued);
    }

    [Fact]
    public async Task QueueAsyncMarksModulePendingWhenExtractionEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata();
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.True(queued);
        Assert.NotNull(metadata.Extraction);
        Assert.Equal("pending", metadata.Extraction.Status);
        Assert.NotNull(metadata.Extraction.LastUpdatedAt);
        Assert.NotNull(metadata.LlmContext);
        Assert.Equal("pending", metadata.LlmContext.Status);
        Assert.NotNull(metadata.LlmContext.LastUpdatedAt);
    }

    [Fact]
    public async Task QueueAsyncRestoresMissingLlmContextStateWhenExtractionEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata { LlmContext = null! };
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.True(queued);
        Assert.NotNull(metadata.LlmContext);
        Assert.Equal("pending", metadata.LlmContext.Status);
        Assert.NotNull(metadata.LlmContext.LastUpdatedAt);
    }

    [Fact]
    public async Task QueueBackfillAsyncQueuesBoundedModulesWhenEnabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.ListModulesForExtractionBackfillAsync(2)).ReturnsAsync([
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "one",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "one.zip",
                Dependencies = []
            },
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "two",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "two.zip",
                Dependencies = []
            }
        ]);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueBackfillAsync(2, CancellationToken.None);

        Assert.Equal(2, queued.Count);
    }

    [Fact]
    public async Task QueueBackfillAsyncMarksQueuedModulesPendingWithoutClearingErrors()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var metadata = new ModuleArtifactMetadata
        {
            Extraction = new ModuleExtractionState { Status = "failed", Error = "tool missing" }
        };

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.ListModulesForExtractionBackfillAsync(1)).ReturnsAsync([
            new ModuleStorage
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                Description = "",
                FilePath = "network.zip",
                Dependencies = [],
                Metadata = metadata
            }
        ]);
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var service = CreateService(config.Object, database.Object);

        var queued = await service.QueueBackfillAsync(1, CancellationToken.None);

        Assert.Single(queued);
        Assert.Equal("pending", metadata.Extraction.Status);
        Assert.Equal("tool missing", metadata.Extraction.Error);
    }

    private static ModuleExtractionService CreateService(
        IModuleExtractionConfigService config,
        IDatabaseService? database = null)
    {
        return new ModuleExtractionService(
            Mock.Of<IModuleService>(),
            database ?? Mock.Of<IDatabaseService>(),
            Mock.Of<IArchiveWorkspaceFactory>(),
            Mock.Of<ITerraformModuleInspector>(),
            Mock.Of<IModuleLlmContextGenerator>(),
            config,
            NullLogger<ModuleExtractionService>.Instance);
    }
}
