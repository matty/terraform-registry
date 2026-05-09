using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class SqliteRuntimeSettingsService(string connectionString) : IRuntimeSettingsService
{
    public async Task<RuntimeSetting?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, value_json, updated_at, updated_by
            FROM runtime_settings
            WHERE key = $key
            """;
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RuntimeSetting
        {
            Key = reader.GetString(0),
            ValueJson = reader.GetString(1),
            UpdatedAt = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            UpdatedBy = reader.IsDBNull(3) ? null : reader.GetString(3)
        };
    }

    public async Task SetAsync(string key, string valueJson, string? updatedBy, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_settings (key, value_json, updated_at, updated_by)
            VALUES ($key, $valueJson, $updatedAt, $updatedBy)
            ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at,
                updated_by = excluded.updated_by
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$valueJson", valueJson);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedBy", (object?)updatedBy ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
