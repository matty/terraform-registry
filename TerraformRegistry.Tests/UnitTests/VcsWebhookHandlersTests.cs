using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class VcsWebhookHandlersTests
{
    private static DefaultHttpContext ContextWithBody(int bodyBytes)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', bodyBytes)));
        return context;
    }

    [Fact]
    public async Task HandleGitHubWebhookRejectsBodyLargerThanOneMiBWithoutCallingVcsService()
    {
        var service = new Mock<IGitHubVcsService>(MockBehavior.Strict);
        var context = ContextWithBody((int)VcsWebhookOptions.DefaultGitHubWebhookMaxBodyBytes + 1);

        var result = await VcsHandlers.HandleGitHubWebhook(service.Object, new VcsWebhookOptions(), context);

        await result.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleGitHubWebhookHonoursAConfiguredLimitBelowTheDefault()
    {
        // The cap is configurable, so a deployment can tighten it below the 1 MiB default.
        var service = new Mock<IGitHubVcsService>(MockBehavior.Strict);
        var options = new VcsWebhookOptions { GitHubWebhookMaxBodyBytes = 512 };
        var context = ContextWithBody(1024);

        var result = await VcsHandlers.HandleGitHubWebhook(service.Object, options, context);

        await result.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleGitHubWebhookRejectsOversizeBodyThatUnderstatesContentLength()
    {
        // Content-Length is attacker-controlled on an endpoint that is unauthenticated until
        // the HMAC is checked, so the streaming counter has to stand on its own.
        var service = new Mock<IGitHubVcsService>(MockBehavior.Strict);
        var options = new VcsWebhookOptions { GitHubWebhookMaxBodyBytes = 512 };
        var context = ContextWithBody(4096);
        context.Request.ContentLength = 8; // a lie

        var result = await VcsHandlers.HandleGitHubWebhook(service.Object, options, context);

        await result.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void ValidateRejectsANonPositiveWebhookLimit()
    {
        Assert.Throws<InvalidOperationException>(
            () => new VcsWebhookOptions { GitHubWebhookMaxBodyBytes = 0 }.Validate());
    }
}
