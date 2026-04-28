using System.Net;

namespace TerraformRegistry.Services;

public interface IWebhookHostResolver
{
    Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsWebhookHostResolver : IWebhookHostResolver
{
    public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
