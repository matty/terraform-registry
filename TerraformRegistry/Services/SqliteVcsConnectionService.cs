using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class SqliteVcsConnectionService : IVcsConnectionService
{
    private readonly string _connectionString;

    public SqliteVcsConnectionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<VcsConnection>> ListConnectionsAsync()
    {
        var connections = new List<VcsConnection>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at FROM vcs_connections ORDER BY created_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            connections.Add(MapConnection(reader));
        }

        return connections;
    }

    public async Task<VcsConnection?> GetConnectionAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at FROM vcs_connections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapConnection(reader);
    }

    public async Task<VcsConnection> CreateConnectionAsync(string? createdBy, string label, string provider, string? patEncrypted, string? defaultOrg, string webhookSecret)
    {
        await using var connection = new SqliteConnection(_connectionString);
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

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO vcs_connections (id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at)
                            VALUES ($id, $label, $provider, $patEncrypted, $defaultOrg, $webhookSecret, $createdBy, $isActive, $createdAt, $updatedAt)";
        cmd.Parameters.AddWithValue("$id", vcsConnection.Id.ToString());
        cmd.Parameters.AddWithValue("$label", vcsConnection.Label);
        cmd.Parameters.AddWithValue("$provider", vcsConnection.Provider);
        cmd.Parameters.AddWithValue("$patEncrypted", (object?)vcsConnection.PatEncrypted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$defaultOrg", (object?)vcsConnection.DefaultOrg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$webhookSecret", vcsConnection.WebhookSecret);
        cmd.Parameters.AddWithValue("$createdBy", (object?)vcsConnection.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$isActive", vcsConnection.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", vcsConnection.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updatedAt", vcsConnection.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync();
        return vcsConnection;
    }

    public async Task<VcsConnection?> UpdateConnectionAsync(Guid id, string? label, string? patEncrypted, string? defaultOrg, bool? isActive)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var setClauses = new List<string> { "updated_at = $updatedAt" };
        var parameters = new List<SqliteParameter>
        {
            new("$id", id.ToString()),
            new("$updatedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
        };

        if (label != null)
        {
            setClauses.Add("label = $label");
            parameters.Add(new SqliteParameter("$label", label));
        }

        if (patEncrypted != null)
        {
            setClauses.Add("pat_encrypted = $patEncrypted");
            parameters.Add(new SqliteParameter("$patEncrypted", patEncrypted));
        }

        if (defaultOrg != null)
        {
            setClauses.Add("default_org = $defaultOrg");
            parameters.Add(new SqliteParameter("$defaultOrg", defaultOrg));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = $isActive");
            parameters.Add(new SqliteParameter("$isActive", isActive.Value ? 1 : 0));
        }

        var sql = $"UPDATE vcs_connections SET {string.Join(", ", setClauses)} WHERE id = $id";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync();

        // Fetch the updated record
        await using var fetchCmd = connection.CreateCommand();
        fetchCmd.CommandText = "SELECT id, label, provider, pat_encrypted, default_org, webhook_secret, created_by, is_active, created_at, updated_at FROM vcs_connections WHERE id = $id";
        fetchCmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await fetchCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapConnection(reader);
    }

    public async Task<bool> DeleteConnectionAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check if any vcs_sources reference this connection
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM vcs_sources WHERE connection_id = $id";
        checkCmd.Parameters.AddWithValue("$id", id.ToString());
        var count = (long)(await checkCmd.ExecuteScalarAsync() ?? 0);
        if (count > 0) return false;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM vcs_connections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IEnumerable<VcsConnection>> ListConnectionSummariesAsync()
    {
        var connections = await ListConnectionsAsync();
        return connections.Where(c => c.IsActive);
    }

    private static VcsConnection MapConnection(SqliteDataReader reader)
    {
        return new VcsConnection
        {
            Id = Guid.Parse(reader.GetString(0)),
            Label = reader.GetString(1),
            Provider = reader.GetString(2),
            PatEncrypted = reader.IsDBNull(3) ? null : reader.GetString(3),
            DefaultOrg = reader.IsDBNull(4) ? null : reader.GetString(4),
            WebhookSecret = reader.GetString(5),
            CreatedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
            IsActive = reader.GetInt32(7) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
            UpdatedAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture)
        };
    }
}
