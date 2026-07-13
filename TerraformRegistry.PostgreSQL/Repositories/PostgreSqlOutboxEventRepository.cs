using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlOutboxEventRepository(string connectionString) : IOutboxEventRepository
{
    public async Task<bool> EnqueueAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO durable_outbox_events (id, kind, idempotency_key, payload_json, state, attempt_count, created_at, updated_at)
            VALUES (@id, @kind, @idempotencyKey, @payloadJson, @state, 0, @createdAt, @updatedAt)
            ON CONFLICT (idempotency_key) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("@id", outboxEvent.Id); command.Parameters.AddWithValue("@kind", outboxEvent.Kind);
        command.Parameters.AddWithValue("@idempotencyKey", outboxEvent.IdempotencyKey); command.Parameters.AddWithValue("@payloadJson", outboxEvent.PayloadJson);
        command.Parameters.AddWithValue("@state", OutboxEventState.Pending); command.Parameters.AddWithValue("@createdAt", outboxEvent.CreatedAt); command.Parameters.AddWithValue("@updatedAt", outboxEvent.UpdatedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OutboxEvent?> TryClaimNextAsync(string ownerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH next AS (SELECT id FROM durable_outbox_events WHERE state IN (@pending, @retry)
                OR (state = @processing AND lease_expires_at <= @now) ORDER BY created_at, id FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE durable_outbox_events e SET state = @processing, owner_id = @ownerId, lease_expires_at = @leaseExpiresAt,
                attempt_count = attempt_count + 1, updated_at = @now FROM next WHERE e.id = next.id
            RETURNING e.id, e.kind, e.idempotency_key, e.payload_json, e.state, e.owner_id, e.lease_expires_at,
                e.attempt_count, e.last_error, e.created_at, e.updated_at, e.delivered_at
            """, connection);
        command.Parameters.AddWithValue("@pending", OutboxEventState.Pending); command.Parameters.AddWithValue("@retry", OutboxEventState.Retry); command.Parameters.AddWithValue("@processing", OutboxEventState.Processing);
        command.Parameters.AddWithValue("@now", now); command.Parameters.AddWithValue("@ownerId", ownerId); command.Parameters.AddWithValue("@leaseExpiresAt", now.Add(leaseDuration));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task<bool> TryCompleteAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) => UpdateAsync(id, ownerId,
        "UPDATE durable_outbox_events SET state = @delivered, owner_id = NULL, lease_expires_at = NULL, delivered_at = @now, updated_at = @now WHERE id = @id AND state = @processing AND owner_id = @ownerId", cancellationToken);
    public Task<bool> TryFailAsync(Guid id, string ownerId, string failureReason, int maximumAttempts, CancellationToken cancellationToken = default) => UpdateAsync(id, ownerId,
        "UPDATE durable_outbox_events SET state = CASE WHEN attempt_count >= @maximumAttempts THEN @deadLetter ELSE @retry END, owner_id = NULL, lease_expires_at = NULL, last_error = @failureReason, updated_at = @now WHERE id = @id AND state = @processing AND owner_id = @ownerId", cancellationToken, failureReason, maximumAttempts);

    private async Task<bool> UpdateAsync(Guid id, string ownerId, string sql, CancellationToken cancellationToken, string? failureReason = null, int maximumAttempts = 0)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@ownerId", ownerId); command.Parameters.AddWithValue("@processing", OutboxEventState.Processing); command.Parameters.AddWithValue("@now", DateTime.UtcNow); command.Parameters.AddWithValue("@delivered", OutboxEventState.Delivered); command.Parameters.AddWithValue("@retry", OutboxEventState.Retry); command.Parameters.AddWithValue("@deadLetter", OutboxEventState.DeadLetter); command.Parameters.AddWithValue("@maximumAttempts", maximumAttempts); command.Parameters.AddWithValue("@failureReason", (object?)failureReason ?? DBNull.Value); return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static OutboxEvent Read(NpgsqlDataReader reader) => new() { Id = reader.GetGuid(0), Kind = reader.GetString(1), IdempotencyKey = reader.GetString(2), PayloadJson = reader.GetString(3), State = reader.GetString(4), OwnerId = reader.IsDBNull(5) ? null : reader.GetString(5), LeaseExpiresAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6), AttemptCount = reader.GetInt32(7), LastError = reader.IsDBNull(8) ? null : reader.GetString(8), CreatedAt = reader.GetDateTime(9), UpdatedAt = reader.GetDateTime(10), DeliveredAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11) };
}
