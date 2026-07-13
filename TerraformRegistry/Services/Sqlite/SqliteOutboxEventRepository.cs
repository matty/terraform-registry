using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteOutboxEventRepository(string connectionString) : IOutboxEventRepository
{
    public async Task<bool> EnqueueAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO durable_outbox_events (id, kind, idempotency_key, payload_json, state, owner_id,
                lease_expires_at, attempt_count, last_error, created_at, updated_at, delivered_at)
            VALUES ($id, $kind, $idempotencyKey, $payloadJson, $state, NULL, NULL, 0, NULL, $createdAt, $updatedAt, NULL)
            ON CONFLICT(idempotency_key) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", outboxEvent.Id.ToString());
        command.Parameters.AddWithValue("$kind", outboxEvent.Kind);
        command.Parameters.AddWithValue("$idempotencyKey", outboxEvent.IdempotencyKey);
        command.Parameters.AddWithValue("$payloadJson", outboxEvent.PayloadJson);
        command.Parameters.AddWithValue("$state", OutboxEventState.Pending);
        command.Parameters.AddWithValue("$createdAt", outboxEvent.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", outboxEvent.UpdatedAt.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OutboxEvent?> TryClaimNextAsync(string ownerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE durable_outbox_events SET state = $processing, owner_id = $ownerId, lease_expires_at = $leaseExpiresAt,
                attempt_count = attempt_count + 1, updated_at = $updatedAt
            WHERE id = (SELECT id FROM durable_outbox_events WHERE state IN ($pending, $retry)
                OR (state = $processing AND lease_expires_at <= $now) ORDER BY created_at, id LIMIT 1)
            RETURNING id, kind, idempotency_key, payload_json, state, owner_id, lease_expires_at, attempt_count,
                last_error, created_at, updated_at, delivered_at
            """;
        command.Parameters.AddWithValue("$processing", OutboxEventState.Processing);
        command.Parameters.AddWithValue("$ownerId", ownerId);
        command.Parameters.AddWithValue("$leaseExpiresAt", now.Add(leaseDuration).ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$pending", OutboxEventState.Pending);
        command.Parameters.AddWithValue("$retry", OutboxEventState.Retry);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task<bool> TryCompleteAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) => UpdateAsync(id, ownerId,
        "UPDATE durable_outbox_events SET state = $delivered, owner_id = NULL, lease_expires_at = NULL, delivered_at = $now, updated_at = $now WHERE id = $id AND state = $processing AND owner_id = $ownerId", cancellationToken);

    public Task<bool> TryFailAsync(Guid id, string ownerId, string failureReason, int maximumAttempts, CancellationToken cancellationToken = default) => UpdateAsync(id, ownerId,
        "UPDATE durable_outbox_events SET state = CASE WHEN attempt_count >= $maximumAttempts THEN $deadLetter ELSE $retry END, owner_id = NULL, lease_expires_at = NULL, last_error = $failureReason, updated_at = $now WHERE id = $id AND state = $processing AND owner_id = $ownerId", cancellationToken, failureReason, maximumAttempts);

    private async Task<bool> UpdateAsync(Guid id, string ownerId, string sql, CancellationToken cancellationToken, string? failureReason = null, int maximumAttempts = 0)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$ownerId", ownerId);
        command.Parameters.AddWithValue("$processing", OutboxEventState.Processing); command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$delivered", OutboxEventState.Delivered); command.Parameters.AddWithValue("$retry", OutboxEventState.Retry);
        command.Parameters.AddWithValue("$deadLetter", OutboxEventState.DeadLetter); command.Parameters.AddWithValue("$maximumAttempts", maximumAttempts);
        command.Parameters.AddWithValue("$failureReason", (object?)failureReason ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static OutboxEvent Read(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Kind = reader.GetString(1),
        IdempotencyKey = reader.GetString(2),
        PayloadJson = reader.GetString(3),
        State = reader.GetString(4),
        OwnerId = reader.IsDBNull(5) ? null : reader.GetString(5),
        LeaseExpiresAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
        AttemptCount = reader.GetInt32(7),
        LastError = reader.IsDBNull(8) ? null : reader.GetString(8),
        CreatedAt = DateTime.Parse(reader.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DeliveredAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
