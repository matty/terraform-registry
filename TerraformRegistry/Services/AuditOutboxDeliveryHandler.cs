using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class AuditOutboxDeliveryHandler(IAuditService auditService) : IOutboxDeliveryHandler
{
    public const string Kind = "audit";

    public bool CanHandle(string kind) => string.Equals(kind, Kind, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AuditOutboxPayload>(outboxEvent.PayloadJson)
            ?? throw new InvalidOperationException("Durable audit event payload is invalid.");
        await auditService.LogAsync(payload.UserId, payload.Action, payload.ResourceType, payload.ResourceId,
            payload.Details, payload.IpAddress);
    }
}

public sealed record AuditOutboxPayload(
    string? UserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    JsonElement? Details,
    string? IpAddress);
