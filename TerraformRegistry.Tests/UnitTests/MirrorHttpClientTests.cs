using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public class MirrorHttpClientTests
{
    [Fact]
    public async Task FetchModuleArchiveAsyncReturnsReadableStreamAndMetadata()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("module-content", Encoding.UTF8, "application/zip")
        });
        var client = CreateClient(handler);

        var result = await client.FetchModuleArchiveAsync(
            "https://github.com/acme/module/archive/main.zip",
            maxBytes: 1024,
            maxRedirects: 3,
            CancellationToken.None);

        using var reader = new StreamReader(result.Content, Encoding.UTF8);
        Assert.Equal("module-content", await reader.ReadToEndAsync(CancellationToken.None));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("https://github.com/acme/module/archive/main.zip", result.FinalUri.ToString());
        Assert.Equal("application/zip", result.ContentType);
    }

    [Fact]
    public async Task FetchModuleArchiveAsyncRejectsRedirectToPrivateNetwork()
    {
        var handler = new SequenceHandler(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://127.0.0.1/module.zip") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("should-not-fetch")
            };
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchModuleArchiveAsync(
            "https://github.com/acme/module/archive/main.zip",
            maxBytes: 1024,
            maxRedirects: 3,
            CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchModuleArchiveAsyncFollowsAllowedRedirect()
    {
        var handler = new SequenceHandler(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://codeload.github.com/acme/module/zip/main") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("zip-bytes")
            };
        });
        var client = CreateClient(handler);

        var result = await client.FetchModuleArchiveAsync(
            "https://github.com/acme/module/archive/main.zip",
            maxBytes: 1024,
            maxRedirects: 3,
            CancellationToken.None);

        Assert.Equal("https://codeload.github.com/acme/module/zip/main", result.FinalUri.ToString());
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.False(request.Headers.Contains("Authorization")));
        AssertPinnedAddress(handler.Requests[0], IPAddress.Parse("93.184.216.34"));
        AssertPinnedAddress(handler.Requests[1], IPAddress.Parse("140.82.112.10"));
    }

    [Fact]
    public async Task FetchModuleArchiveAsyncEnforcesMaxBytesWhileReading()
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("too-large"));
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchModuleArchiveAsync(
            "https://github.com/acme/module/archive/main.zip",
            maxBytes: 3,
            maxRedirects: 3,
            CancellationToken.None));
    }

    [Fact]
    public async Task FetchModuleArchiveAsyncRedactsSignedCurrentUriWhenLoggingExceededSize()
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("too-large"));
        content.Headers.ContentLength = null;
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var logger = new CapturingLogger();
        var client = CreateClient(handler, logger);
        const string signedUrl = "https://github.com/acme/module/archive/main.zip?X-Amz-Credential=access-key&X-Amz-Security-Token=session-token&X-Amz-Signature=secret-signature";

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchModuleArchiveAsync(
            signedUrl,
            maxBytes: 3,
            maxRedirects: 3,
            CancellationToken.None));

        Assert.DoesNotContain("access-key", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("session-token", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-signature", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/acme/module", logger.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-SIGNED-URL]", logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchModuleArchiveAsyncAttachesPinnedAddressesToInitialRequest()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("zip-bytes")
        });
        var client = CreateClient(handler);

        await client.FetchModuleArchiveAsync(
            "https://github.com/acme/module/archive/main.zip",
            maxBytes: 1024,
            maxRedirects: 3,
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        AssertPinnedAddress(request, IPAddress.Parse("93.184.216.34"));
    }

    [Fact]
    public async Task MirrorPinnedConnectionHelperRejectsUnvalidatedRequest()
    {
        var helper = new MirrorPinnedConnectionHelper(new StubStreamConnector());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://github.com/acme/module.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.OpenConnectionAsync(request, 443, CancellationToken.None));
    }

    private static MirrorHttpClient CreateClient(SequenceHandler handler, ILogger<MirrorHttpClient>? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://unused.example")
        };
        var options = new MirrorOptions();
        var configService = new Mock<IMirrorConfigService>();
        configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorConfigResponse { Effective = options });
        var policy = new MirrorPolicyService(
            configService.Object,
            new StubWebhookHostResolver(
                new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)
                {
                    ["github.com"] = [IPAddress.Parse("93.184.216.34")],
                    ["codeload.github.com"] = [IPAddress.Parse("140.82.112.10")]
                }),
            NullLogger<MirrorPolicyService>.Instance);

        return new MirrorHttpClient(
            new TestHttpClientFactory(httpClient),
            policy,
            logger ?? NullLogger<MirrorHttpClient>.Instance);
    }

    private static void AssertPinnedAddress(HttpRequestMessage request, IPAddress expectedAddress)
    {
        Assert.True(request.Options.TryGetValue(
            MirrorPinnedConnectionHelper.ValidatedAddressesOption,
            out IReadOnlyList<IPAddress>? addresses));
        var address = Assert.Single(addresses);
        Assert.Equal(expectedAddress, address);
    }

    private sealed class SequenceHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("TerraformRegistryMirror", name);
            return client;
        }
    }

    private sealed class StubWebhookHostResolver(IReadOnlyDictionary<string, IPAddress[]> addressesByHost) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addressesByHost[host]);
    }

    private sealed class StubStreamConnector : IWebhookStreamConnector
    {
        public ValueTask<Stream> ConnectAsync(
            IReadOnlyList<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken) =>
            new(Stream.Null);
    }

    private sealed class CapturingLogger : ILogger<MirrorHttpClient>
    {
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
    }
}
