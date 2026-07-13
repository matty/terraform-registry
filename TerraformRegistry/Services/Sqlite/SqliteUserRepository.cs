using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteUserRepository(string connectionString) : IUserRepository
{
    public async Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, email, provider, provider_id, is_active, created_at, updated_at
            FROM users
            WHERE lower(email) = lower($email)
            ORDER BY CASE WHEN email = $email THEN 0 ELSE 1 END, created_at ASC
            """;
        cmd.Parameters.AddWithValue("$email", email);

        await using var reader = await cmd.ExecuteReaderAsync();
        var users = new List<User>();
        while (await reader.ReadAsync())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        var users = await GetUsersByEmailCaseInsensitiveAsync(email);
        return users.Count == 0 ? null : users[0];
    }

    public async Task<User?> GetUserByIdAsync(string id)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, email, provider, provider_id, is_active, created_at, updated_at FROM users WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapUser(reader);
    }

    public async Task AddUserAsync(User user)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, email, provider, provider_id, is_active, created_at, updated_at)
            VALUES ($id, $email, $prov, $provId, $isActive, $created, $updated)";

        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$prov", user.Provider);
        cmd.Parameters.AddWithValue("$provId", user.ProviderId);
        cmd.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", user.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", user.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateUserAsync(User user)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE users SET 
                email = $email, provider = $prov, provider_id = $provId, is_active = $isActive,
                updated_at = $updated
            WHERE id = $id";

        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$prov", user.Provider);
        cmd.Parameters.AddWithValue("$provId", user.ProviderId);
        cmd.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$updated", user.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteUserAsync(string userId)
    {
        const string sql = "DELETE FROM users WHERE id = $id";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", userId);
        await command.ExecuteNonQueryAsync();
    }
    public async Task<IEnumerable<User>> ListAllUsersAsync()
    {
        var users = new List<User>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, email, provider, provider_id, is_active, created_at, updated_at FROM users ORDER BY created_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }
    private static User MapUser(SqliteDataReader reader)
    {
        return new User
        {
            Id = reader.GetString(0),
            Email = reader.GetString(1),
            Provider = reader.GetString(2),
            ProviderId = reader.GetString(3),
            IsActive = reader.GetInt64(4) != 0,
            CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            UpdatedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
        };
    }
}
