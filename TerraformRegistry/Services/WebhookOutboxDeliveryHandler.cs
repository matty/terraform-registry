using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class WebhookOutboxDeliveryHandler(WebhookDispatcher dispatcher) : IOutboxDeliveryHandler
{
    public const string Kind = "webhook";

    public bool CanHandle(string kind) => string.Equals(kind, Kind, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(outboxEvent.PayloadJson)
            ?? throw new InvalidOperationException("Durable webhook event payload is invalid.");
        await dispatcher.DeliverAsync(payload, cancellationToken);
    }
}
