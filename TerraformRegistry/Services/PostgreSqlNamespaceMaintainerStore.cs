using Npgsql;

namespace TerraformRegistry.Services;

public sealed class PostgreSqlNamespaceMaintainerStore(string connectionString) : INamespaceMaintainerStore
{
    public async Task<string?> GetMaintainerAsync(string namespaceName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT user_id FROM namespace_maintainers WHERE namespace = @namespace;", connection);
        command.Parameters.AddWithValue("namespace", namespaceName);
        return (string?)await command.ExecuteScalarAsync();
    }

    public async Task AssignMaintainerAsync(string namespaceName, string userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO namespace_maintainers(namespace, user_id) VALUES (@namespace, @userId)
            ON CONFLICT(namespace) DO UPDATE SET user_id = EXCLUDED.user_id, assigned_at = CURRENT_TIMESTAMP;
            """, connection);
        command.Parameters.AddWithValue("namespace", namespaceName);
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync();
    }
}
