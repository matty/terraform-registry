using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class DurableAuditService(IAuditLogStore store, IOutboxEventRepository outbox) : IAuditService
{
    public async Task LogAsync(string? userId, string action, string resourceType, string? resourceId, object? details, string? ipAddress)
    {
        var payload = new AuditOutboxPayload(
            userId,
            action,
            resourceType,
            resourceId,
            details is null ? null : JsonSerializer.SerializeToElement(details),
            ipAddress);
        var now = DateTime.UtcNow;
        var enqueued = await outbox.EnqueueAsync(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            Kind = AuditOutboxDeliveryHandler.Kind,
            IdempotencyKey = $"audit:{Guid.NewGuid():N}",
            PayloadJson = JsonSerializer.Serialize(payload),
            State = OutboxEventState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });
        if (!enqueued) throw new InvalidOperationException("The audit event could not be persisted to the durable outbox.");
    }

    public Task<AuditLogPage> QueryAsync(string? action, string? userId, string? resourceType, DateTime? from, DateTime? toTimestamp,
        int limit = 50, int offset = 0) => store.QueryAsync(action, userId, resourceType, from, toTimestamp, limit, offset);

    public Task<AuditLogEntry?> GetAsync(Guid id) => store.GetAsync(id);
}
