using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public sealed class PostgreSqlRuntimeSettingsService(string connectionString) : IRuntimeSettingsService
{
    public async Task<RuntimeSetting?> GetAsync(string key, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT key, value_json::text, updated_at, updated_by
            FROM runtime_settings
            WHERE key = @key
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RuntimeSetting
        {
            Key = reader.GetString(0),
            ValueJson = reader.GetString(1),
            UpdatedAt = reader.GetDateTime(2),
            UpdatedBy = reader.IsDBNull(3) ? null : reader.GetString(3)
        };
    }

    public async Task SetAsync(string key, string valueJson, string? updatedBy, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO runtime_settings (key, value_json, updated_at, updated_by)
            VALUES (@key, @valueJson, CURRENT_TIMESTAMP, @updatedBy)
            ON CONFLICT(key) DO UPDATE SET
                value_json = EXCLUDED.value_json,
                updated_at = CURRENT_TIMESTAMP,
                updated_by = EXCLUDED.updated_by
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@valueJson", NpgsqlDbType.Jsonb, valueJson);
        command.Parameters.AddWithValue("@updatedBy", (object?)updatedBy ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
