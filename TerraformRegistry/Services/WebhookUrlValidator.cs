using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

public class WebhookUrlValidator(
    IOptions<WebhookSecurityOptions> options,
    IWebhookHostResolver hostResolver,
    ILogger<WebhookUrlValidator> logger)
{
    public async Task<ValidatedWebhookEndpoint> ValidateOutboundWebhookUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Webhook URL must be an absolute URI.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Webhook URL must use http or https.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Webhook URL must not include userinfo credentials.");

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var ipAddress))
        {
            addresses = [ipAddress];
        }
        else
        {
            try
            {
                addresses = await hostResolver.ResolveHostAsync(uri.DnsSafeHost, cancellationToken);
            }
            catch (SocketException ex)
            {
                RegistryLog.Warning(logger, ex, "Webhook host resolution failed for {Host}", uri.DnsSafeHost);
                throw new InvalidOperationException("Webhook URL host could not be resolved.", ex);
            }
        }

        if (addresses.Length == 0)
            throw new InvalidOperationException("Webhook URL host could not be resolved.");

        if (options.Value.AllowPrivateNetworks)
            return new ValidatedWebhookEndpoint(uri, addresses);

        foreach (var address in addresses)
        {
            if (IsPrivateOrLocal(address))
            {
                RegistryLog.Warning(logger, "Blocked webhook target {Url} because it resolved to {Address}", url, address);
                throw new InvalidOperationException("Webhook URL resolves to a private or local address and is not allowed.");
            }
        }

        return new ValidatedWebhookEndpoint(uri, addresses);
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] switch
            {
                0 => true,
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
                192 when bytes[1] == 168 => true,
                100 when bytes[1] >= 64 && bytes[1] <= 127 => true,
                198 when bytes[1] is 18 or 19 => true,
                >= 224 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();

            if (address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                return true;

            if (bytes[0] == 0xfc || bytes[0] == 0xfd)
                return true;

            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;
        }

        return false;
    }
}

public sealed record ValidatedWebhookEndpoint(Uri Uri, IReadOnlyList<IPAddress> Addresses);
