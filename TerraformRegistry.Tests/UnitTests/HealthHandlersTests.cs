using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;

namespace TerraformRegistry.Tests.UnitTests;

public class HealthHandlersTests
{
    [Fact]
    public async Task HandleReadyWithDetailAndAuthIncludesProviderArtifactStorageCheck()
    {
        var database = new Mock<IDatabaseService>();
        database.Setup(service => service.CheckConnectionAsync()).ReturnsAsync(true);

        var modules = new Mock<IModuleService>();
        modules.Setup(service => service.CheckStorageAsync()).ReturnsAsync((true, null));

        var providers = new Mock<IProviderArtifactStorage>();
        providers.Setup(storage => storage.CheckStorageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        var context = CreateAuthenticatedContext();
        var configuration = CreateConfiguration();

        var result = await HealthHandlers.HandleReady(
            database.Object,
            modules.Object,
            providers.Object,
            context,
            configuration);

        var json = await ExecuteAndReadJsonAsync(result, context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("ready", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthy", json.RootElement.GetProperty("checks").GetProperty("storage").GetProperty("status").GetString());
        Assert.Equal("healthy", json.RootElement.GetProperty("checks").GetProperty("providerArtifactStorage").GetProperty("status").GetString());
    }

    [Fact]
    public async Task HandleReadyWhenProviderArtifactStorageIsUnhealthyReturns503WithReason()
    {
        var database = new Mock<IDatabaseService>();
        database.Setup(service => service.CheckConnectionAsync()).ReturnsAsync(true);

        var modules = new Mock<IModuleService>();
        modules.Setup(service => service.CheckStorageAsync()).ReturnsAsync((true, null));

        var providers = new Mock<IProviderArtifactStorage>();
        providers.Setup(storage => storage.CheckStorageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "provider bucket unavailable"));

        var context = CreateAuthenticatedContext();
        var configuration = CreateConfiguration();

        var result = await HealthHandlers.HandleReady(
            database.Object,
            modules.Object,
            providers.Object,
            context,
            configuration);

        var json = await ExecuteAndReadJsonAsync(result, context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("not_ready", json.RootElement.GetProperty("status").GetString());
        var providerStorage = json.RootElement.GetProperty("checks").GetProperty("providerArtifactStorage");
        Assert.Equal("unhealthy", providerStorage.GetProperty("status").GetString());
        Assert.Equal("provider bucket unavailable", providerStorage.GetProperty("reason").GetString());
    }

    private static DefaultHttpContext CreateAuthenticatedContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider()
        };
        context.Request.QueryString = new QueryString("?detail=true");
        context.Request.Headers.Authorization = "Bearer ready-test-token";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["AuthorizationToken"] = "ready-test-token"
            })
            .Build();
    }

    private static async Task<JsonDocument> ExecuteAndReadJsonAsync(IResult result, DefaultHttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
