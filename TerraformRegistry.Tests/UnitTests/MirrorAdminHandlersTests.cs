using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;

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
