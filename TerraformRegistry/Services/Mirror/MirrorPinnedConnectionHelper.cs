using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorPinnedConnectionHelper(IWebhookStreamConnector connector)
{
    public static readonly HttpRequestOptionsKey<IReadOnlyList<IPAddress>> ValidatedAddressesOption =
        new("MirrorValidatedAddresses");

    public static void AttachValidatedAddresses(HttpRequestMessage request, IReadOnlyList<IPAddress> addresses)
    {
        request.Options.Set(ValidatedAddressesOption, addresses);
    }

    public ValueTask<Stream> OpenConnectionAsync(HttpRequestMessage request, int port, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(ValidatedAddressesOption, out IReadOnlyList<IPAddress>? addresses) ||
            addresses.Count == 0)
        {
            throw new InvalidOperationException("Mirror fetch requires validated target addresses.");
        }

        return connector.ConnectAsync(addresses, port, cancellationToken);
    }

    public ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        OpenConnectionAsync(context.InitialRequestMessage, context.DnsEndPoint.Port, cancellationToken);
}
