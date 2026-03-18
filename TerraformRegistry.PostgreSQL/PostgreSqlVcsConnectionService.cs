using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlVcsConnectionService : IVcsConnectionService
{
    private readonly string _connectionString;

    public PostgreSqlVcsConnectionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<VcsConnection>> ListConnectionsAsync()
    {
        var connections = new List<VcsConnection>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at FROM vcs_connections ORDER BY created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            connections.Add(MapConnection(reader));
        }

        return connections;
    }

    public async Task<VcsConnection?> GetConnectionAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at FROM vcs_connections WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapConnection(reader);
    }

    public async Task<VcsConnection> CreateConnectionAsync(string? createdBy, string label, string provider, string? patEncrypted, string? defaultOrg, string webhookSecret)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var vcsConnection = new VcsConnection
        {
            Id = Guid.NewGuid(),
            Label = label,
            Provider = provider,
            PatEncrypted = patEncrypted,
            DefaultOrg = defaultOrg,
            WebhookSecret = webhookSecret,
            CreatedBy = createdBy,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sql = @"INSERT INTO vcs_connections (id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at)
                    VALUES (@id, @label, @provider, @patEncrypted, @defaultOrg, @webhookSecret, @createdBy, @isActive, @createdAt, @updatedAt)";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", vcsConnection.Id);
        cmd.Parameters.AddWithValue("@label", vcsConnection.Label);
        cmd.Parameters.AddWithValue("@provider", vcsConnection.Provider);
        cmd.Parameters.AddWithValue("@patEncrypted", (object?)vcsConnection.PatEncrypted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@defaultOrg", (object?)vcsConnection.DefaultOrg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@webhookSecret", vcsConnection.WebhookSecret);
        cmd.Parameters.AddWithValue("@createdBy", (object?)vcsConnection.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isActive", vcsConnection.IsActive);
        cmd.Parameters.AddWithValue("@createdAt", vcsConnection.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", vcsConnection.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
        return vcsConnection;
    }

    public async Task<VcsConnection?> UpdateConnectionAsync(Guid id, string? label, string? patEncrypted, string? defaultOrg, bool? isActive)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var setClauses = new List<string> { "updated_at = @updatedAt" };
        var parameters = new List<NpgsqlParameter>
        {
            new("@id", id),
            new("@updatedAt", DateTime.UtcNow)
        };

        if (label != null)
        {
            setClauses.Add("label = @label");
            parameters.Add(new NpgsqlParameter("@label", label));
        }

        if (patEncrypted != null)
        {
            setClauses.Add("pat_encrypted = @patEncrypted");
            parameters.Add(new NpgsqlParameter("@patEncrypted", patEncrypted));
        }

        if (defaultOrg != null)
        {
            setClauses.Add("default_org = @defaultOrg");
            parameters.Add(new NpgsqlParameter("@defaultOrg", defaultOrg));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = @isActive");
            parameters.Add(new NpgsqlParameter("@isActive", isActive.Value));
        }

        var sql = $"UPDATE vcs_connections SET {string.Join(", ", setClauses)} WHERE id = @id RETURNING id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at";
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapConnection(reader);
    }

    public async Task<bool> DeleteConnectionAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if any vcs_sources reference this connection
        var checkSql = "SELECT COUNT(*) FROM vcs_sources WHERE connection_id = @id";
        await using var checkCmd = new NpgsqlCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@id", id);
        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0);
        if (count > 0) return false;

        var sql = "DELETE FROM vcs_connections WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IEnumerable<VcsConnection>> ListConnectionSummariesAsync()
    {
        return await ListConnectionsAsync();
    }

    private static VcsConnection MapConnection(NpgsqlDataReader reader)
    {
        return new VcsConnection
        {
            Id = reader.GetGuid(0),
            Label = reader.GetString(1),
            Provider = reader.GetString(2),
            PatEncrypted = reader.IsDBNull(3) ? null : reader.GetString(3),
            DefaultOrg = reader.IsDBNull(4) ? null : reader.GetString(4),
            WebhookSecret = reader.GetString(5),
            CreatedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsActive = reader.GetBoolean(7),
            CreatedAt = reader.GetDateTime(8),
            UpdatedAt = reader.GetDateTime(9)
        };
    }
}
