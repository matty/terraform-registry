using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleDocsHandlersTests
{
    [Fact]
    public async Task SummaryRequiresReadPermission()
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
    public async Task RequeueRequiresManagePermission()
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
    public async Task RegenerateLlmContextRequiresManagePermission()
    {
        var context = CreateContext([Permissions.ModuleDocsRead]);

        var result = await ModuleDocsHandlers.RegenerateLlmContext(
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
    public async Task UpdateConfigRequiresConfigurePermission()
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

    [Fact]
    public async Task RequeuePreservesExistingErrorUntilProcessingStarts()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);
        var metadata = new ModuleArtifactMetadata
        {
            Extraction = new ModuleExtractionState { Status = "failed", Error = "previous failure" }
        };

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetModuleExtractionAdminDetailAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync(new ModuleExtractionAdminDetail
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                Status = "failed",
                Error = "previous failure"
            });
        database.Setup(x => x.UpdateModuleMetadataAsync(
                "acme",
                "network",
                "aws",
                "1.0.0",
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Callback<string, string, string, string, Action<ModuleArtifactMetadata>>((_, _, _, _, mutate) => mutate(metadata))
            .Returns(Task.CompletedTask);

        var extraction = new Mock<IModuleExtractionService>();
        extraction.Setup(x => x.QueueAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await ModuleDocsHandlers.Requeue(
            "acme",
            "network",
            "aws",
            "1.0.0",
            extraction.Object,
            database.Object,
            Mock.Of<IAuditService>(),
            config.Object,
            context);

        Assert.Equal("pending", metadata.Extraction.Status);
        Assert.Equal("previous failure", metadata.Extraction.Error);
    }

    [Fact]
    public async Task RequeueAuditDetailsIncludeCoordinatesAndEnabledState()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetModuleExtractionAdminDetailAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync(new ModuleExtractionAdminDetail
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                Status = "failed"
            });
        database.Setup(x => x.UpdateModuleMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<ModuleArtifactMetadata>>()))
            .Returns(Task.CompletedTask);

        var extraction = new Mock<IModuleExtractionService>();
        extraction.Setup(x => x.QueueAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        object? auditDetails = null;
        var audit = new Mock<IAuditService>();
        audit.Setup(x => x.LogAsync(
                It.IsAny<string?>(),
                "module_docs.requeued",
                "module",
                "acme/network/aws/1.0.0",
                It.IsAny<object?>(),
                It.IsAny<string?>()))
            .Callback<string?, string, string, string?, object?, string?>((_, _, _, _, details, _) => auditDetails = details)
            .Returns(Task.CompletedTask);

        await ModuleDocsHandlers.Requeue(
            "acme",
            "network",
            "aws",
            "1.0.0",
            extraction.Object,
            database.Object,
            audit.Object,
            config.Object,
            context);

        Assert.NotNull(auditDetails);
        Assert.Equal("acme", GetProperty<string>(auditDetails!, "Namespace"));
        Assert.Equal("network", GetProperty<string>(auditDetails!, "Name"));
        Assert.Equal("aws", GetProperty<string>(auditDetails!, "Provider"));
        Assert.Equal("1.0.0", GetProperty<string>(auditDetails!, "Version"));
        Assert.True(GetProperty<bool>(auditDetails!, "Queued"));
        Assert.True(GetProperty<bool>(auditDetails!, "Enabled"));
    }

    [Fact]
    public async Task BackfillAuditDetailsIncludeRequestedLimitQueuedCountAndEnabledState()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);
        context.Request.Body = new MemoryStream("""{"limit":7}"""u8.ToArray());
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = context.Request.Body.Length;

        var extraction = new Mock<IModuleExtractionService>();
        extraction.Setup(x => x.QueueBackfillAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ModuleExtractionRequest("acme", "network", "aws", "1.0.0")]);

        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        object? auditDetails = null;
        var audit = new Mock<IAuditService>();
        audit.Setup(x => x.LogAsync(
                It.IsAny<string?>(),
                "module_docs.backfill_queued",
                "module_docs",
                null,
                It.IsAny<object?>(),
                It.IsAny<string?>()))
            .Callback<string?, string, string, string?, object?, string?>((_, _, _, _, details, _) => auditDetails = details)
            .Returns(Task.CompletedTask);

        await ModuleDocsHandlers.Backfill(
            extraction.Object,
            config.Object,
            audit.Object,
            context,
            context.Request);

        Assert.NotNull(auditDetails);
        Assert.Equal(7, GetProperty<int>(auditDetails!, "RequestedLimit"));
        Assert.Equal(1, GetProperty<int>(auditDetails!, "Queued"));
        Assert.True(GetProperty<bool>(auditDetails!, "Enabled"));
    }

    [Fact]
    public async Task RegenerateLlmContextWithStoredExtractionRegeneratesImmediately()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetModuleExtractionAdminDetailAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync(new ModuleExtractionAdminDetail
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0"
            });
        database.Setup(x => x.GetModuleExtractionAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync(new ModuleExtractionDocument());

        var extraction = new Mock<IModuleExtractionService>();
        extraction.Setup(x => x.RegenerateLlmContextAsync(
                It.Is<ModuleExtractionRequest>(request =>
                    request.Namespace == "acme" &&
                    request.Name == "network" &&
                    request.Provider == "aws" &&
                    request.Version == "1.0.0"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await ModuleDocsHandlers.RegenerateLlmContext(
            "acme",
            "network",
            "aws",
            "1.0.0",
            extraction.Object,
            database.Object,
            Mock.Of<IAuditService>(),
            Mock.Of<IModuleExtractionConfigService>(),
            context);

        var status = await ExecuteForStatusCode(result);
        Assert.Equal(StatusCodes.Status200OK, status);
        extraction.Verify(x => x.RegenerateLlmContextAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        extraction.Verify(x => x.QueueAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegenerateLlmContextWithoutStoredExtractionQueuesFullExtractionWhenEnabled()
    {
        var context = CreateContext([Permissions.ModuleDocsManage]);

        var database = new Mock<IDatabaseService>();
        database.Setup(x => x.GetModuleExtractionAdminDetailAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync(new ModuleExtractionAdminDetail
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0"
            });
        database.Setup(x => x.GetModuleExtractionAsync("acme", "network", "aws", "1.0.0"))
            .ReturnsAsync((ModuleExtractionDocument?)null);

        var extraction = new Mock<IModuleExtractionService>();
        extraction.Setup(x => x.QueueAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var config = new Mock<IModuleExtractionConfigService>();
        config.Setup(x => x.IsEnabledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await ModuleDocsHandlers.RegenerateLlmContext(
            "acme",
            "network",
            "aws",
            "1.0.0",
            extraction.Object,
            database.Object,
            Mock.Of<IAuditService>(),
            config.Object,
            context);

        var status = await ExecuteForStatusCode(result);
        Assert.Equal(StatusCodes.Status202Accepted, status);
        extraction.Verify(x => x.QueueAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        extraction.Verify(x => x.RegenerateLlmContextAsync(It.IsAny<ModuleExtractionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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

    private static T GetProperty<T>(object source, string name)
    {
        var property = source.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(source));
    }
}
