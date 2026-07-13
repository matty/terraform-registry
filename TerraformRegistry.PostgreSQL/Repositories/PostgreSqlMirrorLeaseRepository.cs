using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlMirrorLeaseRepository(string connectionString) : IMirrorLeaseRepository
{
    public async Task<IReadOnlyList<MirrorCacheLease>> ListLeasesAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at
            FROM mirror_cache_leases
            ORDER BY expires_at, lease_key
            LIMIT @limit OFFSET @offset";

        var leases = new List<MirrorCacheLease>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));
        command.Parameters.AddWithValue("@offset", Math.Max(0, offset));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            leases.Add(MapLease(reader));
        }

        return leases;
    }

    public async Task<MirrorCacheLease?> GetLeaseAsync(
        string leaseKey,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at
            FROM mirror_cache_leases
            WHERE lease_key = @leaseKey";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@leaseKey", leaseKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapLease(reader) : null;
    }

    public async Task UpsertLeaseAsync(
        MirrorCacheLease lease,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO mirror_cache_leases (
                id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at)
            VALUES (
                @id, @leaseKey, @operationType, @ownerInstanceId, @expiresAt, @heartbeatAt, @createdAt, @updatedAt)
            ON CONFLICT(lease_key) DO UPDATE SET
                id = EXCLUDED.id,
                operation_type = EXCLUDED.operation_type,
                owner_instance_id = EXCLUDED.owner_instance_id,
                expires_at = EXCLUDED.expires_at,
                heartbeat_at = EXCLUDED.heartbeat_at,
                updated_at = EXCLUDED.updated_at";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
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

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO mirror_cache_leases (
                id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at)
            VALUES (
                @id, @leaseKey, @operationType, @ownerInstanceId, @expiresAt, @heartbeatAt, @createdAt, @updatedAt)
            ON CONFLICT(lease_key) DO UPDATE SET
                id = EXCLUDED.id,
                operation_type = EXCLUDED.operation_type,
                owner_instance_id = EXCLUDED.owner_instance_id,
                expires_at = EXCLUDED.expires_at,
                heartbeat_at = EXCLUDED.heartbeat_at,
                updated_at = EXCLUDED.updated_at
            WHERE mirror_cache_leases.expires_at < @now
            RETURNING id, lease_key, operation_type, owner_instance_id, expires_at, heartbeat_at, created_at, updated_at";

        await using var command = new NpgsqlCommand(sql, connection);
        AddLeaseParameters(command, acquired);
        command.Parameters.AddWithValue("@now", now);

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
        const string sql = @"
            UPDATE mirror_cache_leases
            SET expires_at = @expiresAt,
                heartbeat_at = @heartbeatAt,
                updated_at = @updatedAt
            WHERE lease_key = @leaseKey
              AND id = @id
              AND owner_instance_id = @ownerInstanceId
              AND expires_at > @now";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", leaseId);
        command.Parameters.AddWithValue("@leaseKey", leaseKey);
        command.Parameters.AddWithValue("@ownerInstanceId", ownerInstanceId);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@expiresAt", now.Add(ttl));
        command.Parameters.AddWithValue("@heartbeatAt", now);
        command.Parameters.AddWithValue("@updatedAt", now);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> ReleaseAsync(
        Guid leaseId,
        string leaseKey,
        string ownerInstanceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM mirror_cache_leases
            WHERE id = @id AND lease_key = @leaseKey AND owner_instance_id = @ownerInstanceId";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", leaseId);
        command.Parameters.AddWithValue("@leaseKey", leaseKey);
        command.Parameters.AddWithValue("@ownerInstanceId", ownerInstanceId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddLeaseParameters(NpgsqlCommand command, MirrorCacheLease lease)
    {
        command.Parameters.AddWithValue("@id", lease.Id);
        command.Parameters.AddWithValue("@leaseKey", lease.LeaseKey);
        command.Parameters.AddWithValue("@operationType", lease.OperationType);
        command.Parameters.AddWithValue("@ownerInstanceId", lease.OwnerInstanceId);
        command.Parameters.AddWithValue("@expiresAt", lease.ExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("@heartbeatAt", lease.HeartbeatAt.HasValue
            ? lease.HeartbeatAt.Value.ToUniversalTime()
            : DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", lease.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("@updatedAt", lease.UpdatedAt.ToUniversalTime());
    }

    private static MirrorCacheLease MapLease(NpgsqlDataReader reader)
    {
        return new MirrorCacheLease
        {
            Id = reader.GetGuid(0),
            LeaseKey = reader.GetString(1),
            OperationType = reader.GetString(2),
            OwnerInstanceId = reader.GetString(3),
            ExpiresAt = reader.GetDateTime(4),
            HeartbeatAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.GetDateTime(7)
        };
    }
}
