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
    public async Task QueueAsync_ReturnsFalseAndDoesNotQueueWhenExtractionDisabled()
    {
        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = CreateService(config.Object);

        var queued = await service.QueueAsync(new ModuleExtractionRequest("acme", "network", "aws", "1.0.0"),
            CancellationToken.None);

        Assert.False(queued);
    }

    [Fact]
    public async Task QueueBackfillAsync_QueuesBoundedModulesWhenEnabled()
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

    private static ModuleExtractionService CreateService(
        IModuleExtractionConfigService config,
        IDatabaseService? database = null)
    {
        return new ModuleExtractionService(
            Mock.Of<IModuleService>(),
            database ?? Mock.Of<IDatabaseService>(),
            Mock.Of<IArchiveWorkspaceFactory>(),
            Mock.Of<ITerraformModuleInspector>(),
            config,
            NullLogger<ModuleExtractionService>.Instance);
    }
}
