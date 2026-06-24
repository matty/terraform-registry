using Microsoft.AspNetCore.Http;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleHandlersPaginationTests
{
    [Fact]
    public async Task ListModulesClampsOffsetAndLimitBeforeCallingService()
    {
        ModuleSearchRequest? captured = null;
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListModulesAsync(It.IsAny<ModuleSearchRequest>()))
            .Callback<ModuleSearchRequest>(request => captured = request)
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        await ModuleHandlers.ListModules(
            service.Object,
            new DefaultHttpContext(),
            offset: -10,
            limit: 500);

        Assert.NotNull(captured);
        Assert.Equal(0, captured.Offset);
        Assert.Equal(100, captured.Limit);
    }

    [Fact]
    public async Task ListDeletedModulesClampsOffsetAndLimitBeforeCallingService()
    {
        ModuleSearchRequest? captured = null;
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListDeletedModulesAsync(It.IsAny<ModuleSearchRequest>()))
            .Callback<ModuleSearchRequest>(request => captured = request)
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        await ModuleHandlers.ListDeletedModules(
            service.Object,
            new DefaultHttpContext(),
            offset: -10,
            limit: 500);

        Assert.NotNull(captured);
        Assert.Equal(0, captured.Offset);
        Assert.Equal(100, captured.Limit);
    }
}
