using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class VcsWebhookHandlersTests
{
    [Fact]
    public async Task HandleGitHubWebhookRejectsBodyLargerThanOneMiBWithoutCallingVcsService()
    {
        var service = new Mock<IGitHubVcsService>(MockBehavior.Strict);
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 1024 * 1024 + 1)));

        var result = await VcsHandlers.HandleGitHubWebhook(service.Object, context);

        await result.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        service.VerifyNoOtherCalls();
    }
}
