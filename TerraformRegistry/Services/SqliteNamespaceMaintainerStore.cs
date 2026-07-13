using Microsoft.Data.Sqlite;

namespace TerraformRegistry.Services;

public sealed class SqliteNamespaceMaintainerStore(string connectionString) : INamespaceMaintainerStore
{
    public async Task<string?> GetMaintainerAsync(string namespaceName)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM namespace_maintainers WHERE namespace = $namespace;";
        command.Parameters.AddWithValue("$namespace", namespaceName);
        return (string?)await command.ExecuteScalarAsync();
    }

    public async Task AssignMaintainerAsync(string namespaceName, string userId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO namespace_maintainers(namespace, user_id, assigned_at) VALUES ($namespace, $userId, $assignedAt)
            ON CONFLICT(namespace) DO UPDATE SET user_id = excluded.user_id, assigned_at = excluded.assigned_at;
            """;
        command.Parameters.AddWithValue("$namespace", namespaceName);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$assignedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
