using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleDocsHandlersTests
{
    [Fact]
    public async Task Summary_RequiresReadPermission()
    {
        var context = CreateContext([]);

        var result = await ModuleDocsHandlers.GetSummary(
            Mock.Of<IDatabaseService>(),
            Mock.Of<IModuleExtractionConfigService>(),
            context);

        var status = await ExecuteForStatusCode(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Requeue_RequiresManagePermission()
    {
        var context = CreateContext([Permissions.ModuleDocsRead]);

        var result = await ModuleDocsHandlers.Requeue(
            "acme",
            "network",
            "aws",
            "1.0.0",
            Mock.Of<IModuleExtractionService>(),
            Mock.Of<IDatabaseService>(),
            Mock.Of<IAuditService>(),
            Mock.Of<IModuleExtractionConfigService>(),
            context);

        var status = await ExecuteForStatusCode(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task UpdateConfig_RequiresConfigurePermission()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);
        context.Request.Body = new MemoryStream("""{"enabled":false}"""u8.ToArray());
        context.Request.ContentType = "application/json";

        var result = await ModuleDocsHandlers.UpdateConfig(
            Mock.Of<IModuleExtractionConfigService>(),
            Mock.Of<IAuditService>(),
            context,
            context.Request);

        var status = await ExecuteForStatusCode(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    private static DefaultHttpContext CreateContext(IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-123")
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
    }

    private static Task<int> ExecuteForStatusCode(IResult result)
    {
        var statusCodeResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        return Task.FromResult(statusCodeResult.StatusCode ?? StatusCodes.Status200OK);
    }
}
