using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorAdminHandlersTests
{
    [Fact]
    public async Task GetConfigRequiresMirrorReadPermission()
    {
        var context = CreateContext([]);

        var result = await MirrorAdminHandlers.GetConfig(Mock.Of<IMirrorConfigService>(), context);

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task UpdateConfigPersistsActorAndAuditsChange()
    {
        var context = CreateContext([Permissions.MirrorConfigure]);
        var config = new Mock<IMirrorConfigService>(MockBehavior.Strict);
        var audit = new Mock<IAuditService>();
        var request = new MirrorConfigUpdateRequest { Enabled = true };
        config.Setup(x => x.UpdateConfigAsync(request, "operator-1", context.RequestAborted))
            .ReturnsAsync(new MirrorConfigResponse { Effective = new MirrorOptions { Enabled = true } });

        var result = await MirrorAdminHandlers.UpdateConfig(config.Object, audit.Object, context, request);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        config.VerifyAll();
        audit.Verify(x => x.LogAsync(
            "operator-1",
            "mirror.config_updated",
            "mirror",
            "config",
            It.IsAny<object>(),
            null), Times.Once);
    }

    [Fact]
    public async Task ListLeasesReturnsBoundedPageForMirrorReaders()
    {
        var context = CreateContext([Permissions.MirrorRead]);
        var leases = new Mock<IMirrorLeaseRepository>(MockBehavior.Strict);
        leases.Setup(x => x.ListLeasesAsync(25, 0, context.RequestAborted))
            .ReturnsAsync([new MirrorCacheLease
            {
                LeaseKey = "provider:registry.terraform.io:hashicorp:aws:5.0.0:linux:amd64",
                OperationType = "download",
                OwnerInstanceId = "worker-1",
                ExpiresAt = DateTime.UtcNow.AddMinutes(1)
            }]);

        var result = await MirrorAdminHandlers.ListLeases(leases.Object, context, 25, 0);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        leases.VerifyAll();
    }

    [Fact]
    public async Task ListProviderCacheRequiresMirrorReadAndClampsPagination()
    {
        var context = CreateContext([Permissions.MirrorRead]);
        var providers = new Mock<IProviderMirrorRepository>(MockBehavior.Strict);
        providers.Setup(x => x.ListProviderPackagesAsync("aws", "ready", 100, 0))
            .ReturnsAsync([]);

        var result = await MirrorAdminHandlers.ListProviderCache(providers.Object, context, "aws", "ready", 500, -1);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        providers.VerifyAll();
    }

    [Fact]
    public async Task ListModuleCacheRequiresMirrorReadAndClampsPagination()
    {
        var context = CreateContext([Permissions.MirrorRead]);
        var modules = new Mock<IModuleMirrorRepository>(MockBehavior.Strict);
        modules.Setup(x => x.ListModulePackagesAsync("vpc", "ready", 100, 0))
            .ReturnsAsync([]);

        var result = await MirrorAdminHandlers.ListModuleCache(modules.Object, context, "vpc", "ready", 500, -1);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        modules.VerifyAll();
    }

    [Fact]
    public async Task PurgeProviderReturnsConflictForAnInUseEntry()
    {
        var context = CreateContext([Permissions.MirrorManage]);
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.GetProviderPackageAsync("registry.example.com", "hashicorp", "aws", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(new MirrorProviderPackage
            {
                Hostname = "registry.example.com",
                Namespace = "hashicorp",
                Type = "aws",
                Version = "1.0.0",
                Os = "linux",
                Arch = "amd64",
                DownloadUrl = "https://registry.example.com/package",
                PackageStoragePath = "cache/aws"
            });
        var usage = new MirrorCacheUsage();
        using var lease = usage.Acquire("provider:registry.example.com:hashicorp:aws:1.0.0:linux:amd64");
        var budget = new MirrorCacheBudgetService(providers.Object, Mock.Of<IModuleMirrorRepository>(),
            Mock.Of<IProviderArtifactStorage>(), Mock.Of<IModuleService>(), usage);
        var audit = new Mock<IAuditService>();

        var result = await MirrorAdminHandlers.PurgeProvider(budget, audit.Object, context,
            "registry.example.com", "hashicorp", "aws", "1.0.0", "linux", "amd64");

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PurgeProviderAuditsSuccessfulDeletion()
    {
        var context = CreateContext([Permissions.MirrorManage]);
        var package = new MirrorProviderPackage
        {
            Hostname = "registry.example.com",
            Namespace = "hashicorp",
            Type = "aws",
            Version = "1.0.0",
            Os = "linux",
            Arch = "amd64",
            DownloadUrl = "https://registry.example.com/package",
            PackageStoragePath = "cache/aws"
        };
        var providers = new Mock<IProviderMirrorRepository>();
        providers.Setup(x => x.GetProviderPackageAsync(package.Hostname, package.Namespace, package.Type, package.Version,
                package.Os, package.Arch))
            .ReturnsAsync(package);
        providers.Setup(x => x.UpsertProviderPackageAsync(It.Is<MirrorProviderPackage>(item =>
                item.PackageStoragePath == null && item.State == "evicted")))
            .Returns(Task.CompletedTask);
        var storage = new Mock<IProviderArtifactStorage>();
        storage.Setup(x => x.DeleteAsync("cache/aws", context.RequestAborted)).ReturnsAsync(true);
        var budget = new MirrorCacheBudgetService(providers.Object, Mock.Of<IModuleMirrorRepository>(), storage.Object,
            Mock.Of<IModuleService>(), new MirrorCacheUsage());
        var audit = new Mock<IAuditService>();

        var result = await MirrorAdminHandlers.PurgeProvider(budget, audit.Object, context,
            package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch);

        Assert.Equal(StatusCodes.Status204NoContent, ((IStatusCodeHttpResult)result).StatusCode);
        audit.Verify(x => x.LogAsync("operator-1", "mirror.provider_purged", "mirror_provider",
            "registry.example.com/hashicorp/aws/1.0.0/linux/amd64", null, null), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleReturnsConflictForAnInUseEntry()
    {
        var context = CreateContext([Permissions.MirrorManage]);
        var package = new MirrorModulePackage
        {
            Hostname = "registry.example.com",
            Namespace = "terraform-aws-modules",
            Name = "vpc",
            Provider = "aws",
            Version = "1.0.0",
            DownloadUrl = "https://registry.example.com/package",
            PackageStoragePath = "cache/vpc"
        };
        var modules = new Mock<IModuleMirrorRepository>();
        modules.Setup(x => x.GetModulePackageAsync(package.Hostname, package.Namespace, package.Name, package.Provider,
                package.Version))
            .ReturnsAsync(package);
        var usage = new MirrorCacheUsage();
        using var lease = usage.Acquire("module:registry.example.com:terraform-aws-modules:vpc:aws:1.0.0");
        var budget = new MirrorCacheBudgetService(Mock.Of<IProviderMirrorRepository>(), modules.Object,
            Mock.Of<IProviderArtifactStorage>(), Mock.Of<IModuleService>(), usage);
        var audit = new Mock<IAuditService>();

        var result = await MirrorAdminHandlers.PurgeModule(budget, audit.Object, context,
            package.Hostname, package.Namespace, package.Name, package.Provider, package.Version);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        audit.VerifyNoOtherCalls();
    }

    private static DefaultHttpContext CreateContext(string[] permissions)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "operator-1"),
            .. permissions.Select(permission => new Claim("permission", permission))
        ], "test"));
        return context;
    }
}
