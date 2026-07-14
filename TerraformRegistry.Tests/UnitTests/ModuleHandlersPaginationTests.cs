using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleHandlersPaginationTests
{
    [Fact]
    public async Task ListModulesClampsOffsetAndLimitBeforeCallingService()
    {
        ModuleSearchRequest? captured = null;
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListModulesAsync(It.IsAny<ModuleSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ModuleSearchRequest, CancellationToken>((request, _) => captured = request)
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
    public async Task ListModulesPassesRequestAbortedToModuleService()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken captured = default;
        var context = new DefaultHttpContext();
        context.RequestAborted = cancellation.Token;
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListModulesAsync(It.IsAny<ModuleSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ModuleSearchRequest, CancellationToken>((_, token) => captured = token)
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        await ModuleHandlers.ListModules(service.Object, context);

        Assert.Equal(cancellation.Token, captured);
    }

    [Fact]
    public async Task GetModulePassesRequestAbortedToLocalAndMirrorServices()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var module = new TerraformModule
        {
            Id = "ns/name/aws/1.0.0", Owner = "ns", Namespace = "ns", Name = "name", Provider = "aws",
            Version = "1.0.0", PublishedAt = "", Versions = [], Root = "main", Submodules = [], Providers = []
        };
        var local = new Mock<IModuleService>();
        local.Setup(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", cancellation.Token)).ReturnsAsync(module);
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", module, cancellation.Token)).ReturnsAsync(module);

        await ModuleHandlers.GetModule("ns", "name", "aws", "1.0.0", local.Object, mirror.Object, context);

        local.Verify(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", cancellation.Token), Times.Once);
        mirror.Verify(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", module, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleVersionsPassesRequestAbortedToLocalAndMirrorServices()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var versions = CreateVersions("1.0.0");
        var local = new Mock<IModuleService>();
        local.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token)).ReturnsAsync(versions);
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", versions, cancellation.Token)).ReturnsAsync(versions);

        await ModuleHandlers.GetModuleVersions("ns", "name", "aws", local.Object, mirror.Object, context);

        local.Verify(x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token), Times.Once);
        mirror.Verify(x => x.GetModuleVersionsAsync("ns", "name", "aws", versions, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task DownloadModulePassesRequestAbortedToLocalAndMirrorServices()
    {
        using var cancellation = new CancellationTokenSource();
        using var analytics = new ModuleDownloadAnalyticsBuffer(Options.Create(new DownloadAnalyticsOptions()));
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var local = new Mock<IModuleService>();
        local.Setup(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", cancellation.Token)).ReturnsAsync("/local");
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", "/local", cancellation.Token)).ReturnsAsync("/download");

        await ModuleHandlers.DownloadModule("ns", "name", "aws", "1.0.0", local.Object, mirror.Object, analytics, context);

        local.Verify(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", cancellation.Token), Times.Once);
        mirror.Verify(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", "/local", cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task DownloadLatestModulePassesRequestAbortedToBothLookupStages()
    {
        using var cancellation = new CancellationTokenSource();
        using var analytics = new ModuleDownloadAnalyticsBuffer(Options.Create(new DownloadAnalyticsOptions()));
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var versions = CreateVersions("1.0.0");
        var local = new Mock<IModuleService>();
        local.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token)).ReturnsAsync(versions);
        local.Setup(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", cancellation.Token)).ReturnsAsync("/local");
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", versions, cancellation.Token)).ReturnsAsync(versions);
        mirror.Setup(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", "/local", cancellation.Token)).ReturnsAsync("/download");

        await ModuleHandlers.DownloadLatestModule("ns", "name", "aws", local.Object, mirror.Object, analytics, context);

        local.Verify(x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token), Times.Once);
        local.Verify(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", cancellation.Token), Times.Once);
        mirror.Verify(x => x.GetModuleVersionsAsync("ns", "name", "aws", versions, cancellation.Token), Times.Once);
        mirror.Verify(x => x.GetModuleDownloadPathAsync("ns", "name", "aws", "1.0.0", "/local", cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ListDeletedModulesClampsOffsetAndLimitBeforeCallingService()
    {
        ModuleSearchRequest? captured = null;
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListDeletedModulesAsync(It.IsAny<ModuleSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ModuleSearchRequest, CancellationToken>((request, _) => captured = request)
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

    [Fact]
    public async Task ListDeletedModulesPassesRequestAbortedToModuleService()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("permission", Permissions.ModulesDelete)], "test"));
        var service = new Mock<IModuleService>();
        service.Setup(x => x.ListDeletedModulesAsync(It.IsAny<ModuleSearchRequest>(), cancellation.Token))
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        await ModuleHandlers.ListDeletedModules(service.Object, context);

        service.Verify(x => x.ListDeletedModulesAsync(It.IsAny<ModuleSearchRequest>(), cancellation.Token), Times.Once);
    }

    private static ModuleVersions CreateVersions(string version) => new()
    {
        Modules = [new ModuleVersionInfo { Versions = [new VersionInfo { Version = version }] }]
    };
}
