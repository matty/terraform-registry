using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_SavesDocumentAndMarksModuleSucceeded()
    {
        var moduleService = new Mock<IModuleService>();
        moduleService
            .Setup(x => x.OpenModulePackageStreamAsync("acme", "network", "aws", "1.2.3"))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"module-extraction-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var workspaceFactory = new Mock<IArchiveWorkspaceFactory>();
        workspaceFactory
            .Setup(x => x.CreateAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArchiveWorkspace(tempRoot, tempRoot));

        var document = new ModuleExtractionDocument
        {
            Readme = new ModuleReadmeDocument { Path = "README.md", Title = "Network" },
            Inputs =
            [
                new ModuleInputDefinition { Name = "cidr", Required = true }
            ],
            Outputs =
            [
                new ModuleOutputDefinition { Name = "id" }
            ],
            ProviderRequirements =
            [
                new ModuleProviderRequirement
                {
                    Name = "aws",
                    Namespace = "hashicorp",
                    Source = "hashicorp/aws",
                    VersionConstraint = ">= 5.0"
                }
            ],
            Submodules =
            [
                new ModuleSubmodule { Path = "modules/vpc", Providers = new Dictionary<string, string>() }
            ],
            Examples =
            [
                new ModuleExampleDefinition { Name = "basic", Path = "examples/basic" }
            ]
        };

        var inspector = new Mock<ITerraformModuleInspector>();
        inspector
            .Setup(x => x.InspectAsync(tempRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var metadataUpdates = new List<ModuleArtifactMetadata>();
        var database = new Mock<IDatabaseService>();
        database
            .Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.2.3",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) =>
            {
                var metadata = new ModuleArtifactMetadata();
                mutate(metadata);
                metadataUpdates.Add(metadata);
            })
            .Returns(Task.CompletedTask);

        var service = new ModuleExtractionService(
            moduleService.Object,
            database.Object,
            workspaceFactory.Object,
            inspector.Object,
            Mock.Of<IModuleExtractionConfigService>(),
            NullLogger<ModuleExtractionService>.Instance);

        await service.ExtractAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.2.3"),
            CancellationToken.None);

        database.Verify(x => x.UpsertModuleExtractionAsync(
            "acme",
            "network",
            "aws",
            "1.2.3",
            document,
            null), Times.Once);

        Assert.Collection(metadataUpdates,
            processing => Assert.Equal("processing", processing.Extraction.Status),
            succeeded =>
            {
                Assert.Equal("succeeded", succeeded.Extraction.Status);
                Assert.Equal("README.md", succeeded.Documentation!.PrimaryReadmePath);
                Assert.Equal(1, succeeded.Documentation.InputCount);
                Assert.Equal(1, succeeded.Documentation.OutputCount);
                Assert.Equal(1, succeeded.Documentation.ExampleCount);
                Assert.True(succeeded.Documentation.HasSubmoduleDocs);
                Assert.Single(succeeded.ProviderRequirements);
                Assert.Single(succeeded.Submodules);
            });

        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task ExtractAsync_MarksModuleFailedWhenInspectionFails()
    {
        var moduleService = new Mock<IModuleService>();
        moduleService
            .Setup(x => x.OpenModulePackageStreamAsync("acme", "network", "aws", "1.2.3"))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"module-extraction-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var workspaceFactory = new Mock<IArchiveWorkspaceFactory>();
        workspaceFactory
            .Setup(x => x.CreateAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArchiveWorkspace(tempRoot, tempRoot));

        var inspector = new Mock<ITerraformModuleInspector>();
        inspector
            .Setup(x => x.InspectAsync(tempRoot, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tool missing"));

        var metadataUpdates = new List<ModuleArtifactMetadata>();
        var database = new Mock<IDatabaseService>();
        database
            .Setup(x => x.UpdateModuleMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) =>
            {
                var metadata = new ModuleArtifactMetadata();
                mutate(metadata);
                metadataUpdates.Add(metadata);
            })
            .Returns(Task.CompletedTask);

        var service = new ModuleExtractionService(
            moduleService.Object,
            database.Object,
            workspaceFactory.Object,
            inspector.Object,
            Mock.Of<IModuleExtractionConfigService>(),
            NullLogger<ModuleExtractionService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.2.3"),
                CancellationToken.None));

        Assert.Equal("tool missing", ex.Message);
        Assert.Collection(metadataUpdates,
            processing => Assert.Equal("processing", processing.Extraction.Status),
            failed =>
            {
                Assert.Equal("failed", failed.Extraction.Status);
                Assert.Equal("tool missing", failed.Extraction.Error);
            });
    }
}
