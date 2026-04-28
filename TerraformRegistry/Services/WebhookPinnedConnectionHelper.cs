using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace TerraformRegistry.Services;

public interface IWebhookStreamConnector
{
    ValueTask<Stream> ConnectAsync(IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken);
}

public sealed class SocketWebhookStreamConnector : IWebhookStreamConnector
{
    public async ValueTask<Stream> ConnectAsync(IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException("Unable to connect to the validated webhook target.", lastError);
    }
}

public sealed class WebhookPinnedConnectionHelper(IWebhookStreamConnector connector)
{
    public static readonly HttpRequestOptionsKey<IReadOnlyList<IPAddress>> ValidatedAddressesOption =
        new("WebhookValidatedAddresses");

    public static void AttachValidatedAddresses(HttpRequestMessage request, IReadOnlyList<IPAddress> addresses)
    {
        request.Options.Set(ValidatedAddressesOption, addresses);
    }

    public ValueTask<Stream> OpenConnectionAsync(HttpRequestMessage request, int port, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(ValidatedAddressesOption, out IReadOnlyList<IPAddress>? addresses) ||
            addresses.Count == 0)
        {
            throw new InvalidOperationException("Webhook delivery requires validated target addresses.");
        }

        return connector.ConnectAsync(addresses, port, cancellationToken);
    }

    public ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        OpenConnectionAsync(context.InitialRequestMessage, context.DnsEndPoint.Port, cancellationToken);
}
