using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlUserRepository(string connectionString) : IUserRepository
{
    public async Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email)
    {
        const string sql =
            """
            SELECT id, email, provider, provider_id, created_at, updated_at
            FROM users
            WHERE lower(email) = lower(@email)
            ORDER BY CASE WHEN email = @email THEN 0 ELSE 1 END, created_at ASC
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@email", email);

        await using var reader = await command.ExecuteReaderAsync();
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
        const string sql = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users WHERE id = @id";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapUser(reader);
    }

    public async Task AddUserAsync(User user)
    {
        const string sql = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES (@id, @email, @provider, @providerId, @createdAt, @updatedAt)";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", user.Id);
        command.Parameters.AddWithValue("@email", user.Email);
        command.Parameters.AddWithValue("@provider", user.Provider);
        command.Parameters.AddWithValue("@providerId", user.ProviderId);
        command.Parameters.AddWithValue("@createdAt", user.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", user.UpdatedAt);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateUserAsync(User user)
    {
        const string sql =
            "UPDATE users SET email=@email, provider=@provider, provider_id=@providerId, updated_at=@updatedAt WHERE id=@id";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", user.Id);
        command.Parameters.AddWithValue("@email", user.Email);
        command.Parameters.AddWithValue("@provider", user.Provider);
        command.Parameters.AddWithValue("@providerId", user.ProviderId);
        command.Parameters.AddWithValue("@updatedAt", user.UpdatedAt);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteUserAsync(string userId)
    {
        const string sql = "DELETE FROM users WHERE id = @id";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<User>> ListAllUsersAsync()
    {
        const string sql = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users ORDER BY created_at DESC";
        var users = new List<User>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }
    private static User MapUser(NpgsqlDataReader reader)
    {
        return new User
        {
            Id = reader.GetString(0),
            Email = reader.GetString(1),
            Provider = reader.GetString(2),
            ProviderId = reader.GetString(3),
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5)
        };
    }
}
