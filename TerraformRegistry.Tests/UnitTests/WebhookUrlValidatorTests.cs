using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class WebhookUrlValidatorTests
{
    [Theory]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.com/hook")]
    public async Task ValidateOutboundWebhookUrlAsyncRejectsInvalidSchemesAndUserInfo(string url)
    {
        var validator = new WebhookUrlValidator(
            Options.Create(new WebhookSecurityOptions()),
            new StubWebhookHostResolver(IPAddress.Parse("93.184.216.34")),
            NullLogger<WebhookUrlValidator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateOutboundWebhookUrlAsync(url, CancellationToken.None));

        Assert.Contains("webhook", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task ValidateOutboundWebhookUrlAsyncRejectsPrivateAndLocalTargets(string url)
    {
        var validator = new WebhookUrlValidator(
            Options.Create(new WebhookSecurityOptions()),
            new StubWebhookHostResolver(IPAddress.Loopback),
            NullLogger<WebhookUrlValidator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateOutboundWebhookUrlAsync(url, CancellationToken.None));

        Assert.Contains("webhook", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateOutboundWebhookUrlAsyncAllowsHttpsPublicHost()
    {
        var validator = new WebhookUrlValidator(
            Options.Create(new WebhookSecurityOptions()),
            new StubWebhookHostResolver(IPAddress.Parse("93.184.216.34")),
            NullLogger<WebhookUrlValidator>.Instance);

        var endpoint = await validator.ValidateOutboundWebhookUrlAsync("https://example.com/hook", CancellationToken.None);

        Assert.Equal("https", endpoint.Uri.Scheme);
        Assert.Equal("example.com", endpoint.Uri.Host);
        Assert.Collection(endpoint.Addresses, address => Assert.Equal(IPAddress.Parse("93.184.216.34"), address));
    }

    [Fact]
    public async Task ValidateOutboundWebhookUrlAsyncAllowsPrivateNetworkWhenConfigured()
    {
        var validator = new WebhookUrlValidator(
            Options.Create(new WebhookSecurityOptions { AllowPrivateNetworks = true }),
            new StubWebhookHostResolver(IPAddress.Loopback),
            NullLogger<WebhookUrlValidator>.Instance);

        var endpoint = await validator.ValidateOutboundWebhookUrlAsync("http://localhost/hook", CancellationToken.None);

        Assert.Equal("localhost", endpoint.Uri.Host);
        Assert.Collection(endpoint.Addresses, address => Assert.Equal(IPAddress.Loopback, address));
    }

    private sealed class StubWebhookHostResolver(params IPAddress[] addresses) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }
}
