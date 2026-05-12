using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleExtractionConfigServiceTests
{
    [Fact]
    public async Task GetAsyncUsesStartupDefaultWhenNoRuntimeSettingExists()
    {
        var settings = new Mock<IRuntimeSettingsService>();
        settings
            .Setup(x => x.GetAsync("module_extraction", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuntimeSetting?)null);

        var service = new ModuleExtractionConfigService(
            settings.Object,
            Options.Create(new ModuleExtractionOptions { Enabled = false }));

        var config = await service.GetAsync(CancellationToken.None);

        Assert.False(config.Enabled);
        Assert.Null(config.PersistedEnabled);
        Assert.False(config.HasRuntimeOverride);
    }

    [Fact]
    public async Task GetAsyncRuntimeSettingOverridesStartupDefault()
    {
        var settings = new Mock<IRuntimeSettingsService>();
        settings
            .Setup(x => x.GetAsync("module_extraction", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuntimeSetting
            {
                Key = "module_extraction",
                ValueJson = """{"enabled":false}""",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "admin"
            });

        var service = new ModuleExtractionConfigService(
            settings.Object,
            Options.Create(new ModuleExtractionOptions { Enabled = true }));

        var config = await service.GetAsync(CancellationToken.None);

        Assert.False(config.Enabled);
        Assert.False(config.PersistedEnabled);
        Assert.True(config.HasRuntimeOverride);
        Assert.Equal("admin", config.UpdatedBy);
    }

    [Fact]
    public async Task SetEnabledAsyncPersistsRuntimeOverride()
    {
        var settings = new Mock<IRuntimeSettingsService>();
        var service = new ModuleExtractionConfigService(
            settings.Object,
            Options.Create(new ModuleExtractionOptions { Enabled = true }));

        await service.SetEnabledAsync(false, "user-123", CancellationToken.None);

        settings.Verify(x => x.SetAsync(
            "module_extraction",
            """{"enabled":false}""",
            "user-123",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
