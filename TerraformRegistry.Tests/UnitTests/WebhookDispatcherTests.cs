using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class WebhookDispatcherTests
{
    [Fact]
    public async Task SendTestAsync_ReturnsFailure_OnNonSuccessStatusCode()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var dispatcher = CreateDispatcher(
            handler,
            CreateValidator(IPAddress.Parse("93.184.216.34")));

        var webhook = CreateWebhook("https://example.com/hook");

        var result = await dispatcher.SendTestAsync(webhook);

        Assert.False(result.Success);
        Assert.Contains("502", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTestAsync_AttachesValidatedAddressesToRequest()
    {
        IReadOnlyList<IPAddress>? pinnedAddresses = null;
        var handler = new RecordingHandler(request =>
        {
            Assert.True(request.Options.TryGetValue(WebhookPinnedConnectionHelper.ValidatedAddressesOption, out pinnedAddresses));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var dispatcher = CreateDispatcher(
            handler,
            CreateValidator(IPAddress.Parse("93.184.216.34"), IPAddress.Parse("93.184.216.35")));

        var result = await dispatcher.SendTestAsync(CreateWebhook("https://example.com/hook"));

        Assert.True(result.Success);
        Assert.NotNull(pinnedAddresses);
        Assert.Equal(2, pinnedAddresses!.Count);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), pinnedAddresses[0]);
        Assert.Equal(IPAddress.Parse("93.184.216.35"), pinnedAddresses[1]);
    }

    [Fact]
    public async Task SendTestAsync_ReturnsFailure_WhenDeliveryTimeValidationFails()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = CreateDispatcher(
            handler,
            CreateValidator(IPAddress.Loopback));

        var result = await dispatcher.SendTestAsync(CreateWebhook("http://localhost/hook"));

        Assert.False(result.Success);
        Assert.Contains("private or local", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task OpenConnectionAsync_UsesPinnedAddressesInsteadOfDnsEndpoint()
    {
        var connector = new RecordingConnector();
        var helper = new WebhookPinnedConnectionHelper(connector);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/hook");
        WebhookPinnedConnectionHelper.AttachValidatedAddresses(request, [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("93.184.216.35")]);

        await helper.OpenConnectionAsync(request, 443, CancellationToken.None);

        Assert.NotNull(connector.Addresses);
        Assert.Equal([IPAddress.Parse("93.184.216.34"), IPAddress.Parse("93.184.216.35")], connector.Addresses);
        Assert.Equal(443, connector.Port);
    }

    private static WebhookDispatcher CreateDispatcher(HttpMessageHandler handler, WebhookUrlValidator validator)
    {
        var webhookService = new Mock<IWebhookService>(MockBehavior.Strict);
        var client = new HttpClient(handler);
        var factory = new TestHttpClientFactory(client);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BaseUrl"] = "https://registry.example.com" })
            .Build();

        return new WebhookDispatcher(
            webhookService.Object,
            factory,
            configuration,
            validator,
            NullLogger<WebhookDispatcher>.Instance);
    }

    private static WebhookUrlValidator CreateValidator(params IPAddress[] addresses) =>
        new(
            Options.Create(new WebhookSecurityOptions()),
            new StubWebhookHostResolver(addresses),
            NullLogger<WebhookUrlValidator>.Instance);

    private static Webhook CreateWebhook(string url) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Url = url,
            Events = ["module.published"],
            Format = "generic"
        };

    private sealed class StubWebhookHostResolver(params IPAddress[] addresses) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RecordingConnector : IWebhookStreamConnector
    {
        public IReadOnlyList<IPAddress>? Addresses { get; private set; }
        public int? Port { get; private set; }

        public ValueTask<Stream> ConnectAsync(IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken)
        {
            Addresses = addresses.ToArray();
            Port = port;
            return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("connected")));
        }
    }
}
