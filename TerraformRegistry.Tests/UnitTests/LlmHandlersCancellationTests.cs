using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class LlmHandlersCancellationTests
{
    [Fact]
    public async Task ListModulesPassesRequestAbortedToModuleService()
    {
        using var source = new CancellationTokenSource();
        var context = AuthenticatedContext(source.Token);
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListModulesAsync(It.IsAny<ModuleSearchRequest>(), source.Token))
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        await LlmHandlers.ListModules(service.Object, Configuration(), context);

        service.Verify(x => x.ListModulesAsync(It.IsAny<ModuleSearchRequest>(), source.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleVersionsPassesRequestAbortedToEachRead()
    {
        using var source = new CancellationTokenSource();
        var context = AuthenticatedContext(source.Token);
        var service = new Mock<IModuleService>();
        var database = new Mock<IDatabaseService>();
        service.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", source.Token)).ReturnsAsync(new ModuleVersions
        {
            Modules = [new ModuleVersionInfo { Versions = [new VersionInfo { Version = "1.0.0" }] }]
        });
        database.Setup(x => x.GetModuleLlmContextAsync("ns", "name", "aws", "1.0.0", source.Token))
            .Returns(Task.FromResult<ModuleLlmContextDocument?>(null));

        await LlmHandlers.GetModuleVersions("ns", "name", "aws", service.Object, database.Object, Configuration(), context);

        service.Verify(x => x.GetModuleVersionsAsync("ns", "name", "aws", source.Token), Times.Once);
        database.Verify(x => x.GetModuleLlmContextAsync("ns", "name", "aws", "1.0.0", source.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleContextPassesRequestAbortedToEachDatabaseRead()
    {
        using var source = new CancellationTokenSource();
        var context = AuthenticatedContext(source.Token);
        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", source.Token))
            .ReturnsAsync(new TerraformModule
            {
                Id = "ns/name/aws/1.0.0", Owner = "ns", Namespace = "ns", Name = "name", Provider = "aws",
                Version = "1.0.0", PublishedAt = "", Versions = [], Root = "main", Submodules = [], Providers = []
            });
        database.Setup(x => x.GetModuleLlmContextAsync("ns", "name", "aws", "1.0.0", source.Token))
            .Returns(Task.FromResult<ModuleLlmContextDocument?>(null));

        await LlmHandlers.GetModuleContext("ns", "name", "aws", "1.0.0", database.Object, context);

        database.Verify(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", source.Token), Times.Once);
        database.Verify(x => x.GetModuleLlmContextAsync("ns", "name", "aws", "1.0.0", source.Token), Times.Once);
    }

    private static DefaultHttpContext AuthenticatedContext(CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext { RequestAborted = cancellationToken };
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("permission", Permissions.ModulesRead)], "test"));
        return context;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().Build();
}
