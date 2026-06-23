using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteMirrorLeaseRepository(string connectionString) : IMirrorLeaseRepository
{
    public async Task<MirrorCacheLease?> GetLeaseAsync(
        string leaseKey,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at
            FROM mirror_cache_leases
            WHERE lease_key = $leaseKey";
        command.Parameters.AddWithValue("$leaseKey", leaseKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapLease(reader) : null;
    }

    public async Task UpsertLeaseAsync(
        MirrorCacheLease lease,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_cache_leases (
                id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at)
            VALUES (
                $id, $leaseKey, $operationType, $ownerInstanceId, $expiresAt, $heartbeatAt, $createdAt, $updatedAt)
            ON CONFLICT(lease_key) DO UPDATE SET
                id = excluded.id,
                operation_type = excluded.operation_type,
                owner_instance_id = excluded.owner_instance_id,
                expires_at = excluded.expires_at,
                heartbeat_at = excluded.heartbeat_at,
                updated_at = excluded.updated_at";
        AddLeaseParameters(command, lease);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MirrorCacheLease?> TryAcquireAsync(
        string leaseKey,
        string operationType,
        string ownerInstanceId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var acquired = new MirrorCacheLease
        {
            LeaseKey = leaseKey,
            OperationType = operationType,
            OwnerInstanceId = ownerInstanceId,
            ExpiresAt = now.Add(ttl),
            HeartbeatAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO mirror_cache_leases (
                id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at)
            VALUES (
                $id, $leaseKey, $operationType, $ownerInstanceId, $expiresAt, $heartbeatAt, $createdAt, $updatedAt)
            ON CONFLICT(lease_key) DO UPDATE SET
                id = excluded.id,
                operation_type = excluded.operation_type,
                owner_instance_id = excluded.owner_instance_id,
                expires_at = excluded.expires_at,
                heartbeat_at = excluded.heartbeat_at,
                updated_at = excluded.updated_at
            WHERE mirror_cache_leases.expires_at < $now
            RETURNING id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at";
        AddLeaseParameters(command, acquired);
        command.Parameters.AddWithValue("$now", ToSqliteTimestamp(now));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapLease(reader) : null;
    }

    public async Task<bool> HeartbeatAsync(
        Guid leaseId,
        string leaseKey,
        string ownerInstanceId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE mirror_cache_leases
            SET expires_at = $expiresAt,
                heartbeat_at = $heartbeatAt,
                updated_at = $updatedAt
            WHERE lease_key = $leaseKey
              AND id = $id
              AND owner_instance_id = $ownerInstanceId
              AND expires_at > $now";
        command.Parameters.AddWithValue("$id", leaseId.ToString());
        command.Parameters.AddWithValue("$leaseKey", leaseKey);
        command.Parameters.AddWithValue("$ownerInstanceId", ownerInstanceId);
        command.Parameters.AddWithValue("$now", ToSqliteTimestamp(now));
        command.Parameters.AddWithValue("$expiresAt", ToSqliteTimestamp(now.Add(ttl)));
        command.Parameters.AddWithValue("$heartbeatAt", ToSqliteTimestamp(now));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(now));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> ReleaseAsync(
        Guid leaseId,
        string leaseKey,
        string ownerInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM mirror_cache_leases
            WHERE id = $id AND lease_key = $leaseKey AND owner_instance_id = $ownerInstanceId";
        command.Parameters.AddWithValue("$id", leaseId.ToString());
        command.Parameters.AddWithValue("$leaseKey", leaseKey);
        command.Parameters.AddWithValue("$ownerInstanceId", ownerInstanceId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddLeaseParameters(SqliteCommand command, MirrorCacheLease lease)
    {
        command.Parameters.AddWithValue("$id", lease.Id.ToString());
        command.Parameters.AddWithValue("$leaseKey", lease.LeaseKey);
        command.Parameters.AddWithValue("$operationType", lease.OperationType);
        command.Parameters.AddWithValue("$ownerInstanceId", lease.OwnerInstanceId);
        command.Parameters.AddWithValue("$expiresAt", ToSqliteTimestamp(lease.ExpiresAt));
        command.Parameters.AddWithValue("$heartbeatAt", lease.HeartbeatAt.HasValue
            ? ToSqliteTimestamp(lease.HeartbeatAt.Value)
            : DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", ToSqliteTimestamp(lease.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ToSqliteTimestamp(lease.UpdatedAt));
    }

    private static MirrorCacheLease MapLease(SqliteDataReader reader)
    {
        return new MirrorCacheLease
        {
            Id = Guid.Parse(reader.GetString(0)),
            LeaseKey = reader.GetString(1),
            OperationType = reader.GetString(2),
            OwnerInstanceId = reader.GetString(3),
            ExpiresAt = ReadRequiredDateTime(reader, 4),
            HeartbeatAt = reader.IsDBNull(5) ? null : ReadRequiredDateTime(reader, 5),
            CreatedAt = ReadRequiredDateTime(reader, 6),
            UpdatedAt = ReadRequiredDateTime(reader, 7)
        };
    }

    private static DateTime ReadRequiredDateTime(SqliteDataReader reader, int ordinal) =>
        DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ToSqliteTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
