using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlApiKeyRepository(string connectionString) : IApiKeyRepository
{
    public async Task AddApiKeyAsync(ApiKey apiKey)
    {
        const string sql = @"
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at)
            VALUES (@id, @userId, @description, @tokenHash, @prefix, @isShared, @createdAt, @expiresAt, @lastUsedAt)";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", apiKey.Id);
        command.Parameters.AddWithValue("@userId", apiKey.UserId);
        command.Parameters.AddWithValue("@description", apiKey.Description);
        command.Parameters.AddWithValue("@tokenHash", apiKey.TokenHash);
        command.Parameters.AddWithValue("@prefix", apiKey.Prefix);
        command.Parameters.AddWithValue("@isShared", apiKey.IsShared);
        command.Parameters.AddWithValue("@createdAt", apiKey.CreatedAt);
        command.Parameters.AddWithValue("@expiresAt", apiKey.ExpiresAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@lastUsedAt", apiKey.LastUsedAt ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<ApiKey?> GetApiKeyAsync(Guid id)
    {
        const string sql =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE id = @id";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapReaderToApiKey(reader);
    }

    public async Task<IEnumerable<ApiKey>> GetApiKeysByUserAsync(string userId)
    {
        const string sql =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE user_id = @userId ORDER BY created_at DESC";
        var keys = new List<ApiKey>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapReaderToApiKey(reader));
        }

        return keys;
    }

    public async Task<IEnumerable<ApiKey>> GetSharedApiKeysAsync()
    {
        const string sql =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE is_shared = TRUE ORDER BY created_at DESC";
        var keys = new List<ApiKey>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapReaderToApiKey(reader));
        }

        return keys;
    }

    public async Task<IEnumerable<ApiKey>> GetApiKeysByPrefixAsync(string prefix)
    {
        const string sql =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE prefix = @prefix";
        var keys = new List<ApiKey>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@prefix", prefix);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapReaderToApiKey(reader));
        }

        return keys;
    }

    public async Task UpdateApiKeyAsync(ApiKey apiKey)
    {
        const string sql =
            "UPDATE api_keys SET description=@description, is_shared=@isShared, expires_at=@expiresAt, last_used_at=@lastUsedAt WHERE id=@id";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", apiKey.Id);
        command.Parameters.AddWithValue("@description", apiKey.Description);
        command.Parameters.AddWithValue("@isShared", apiKey.IsShared);
        command.Parameters.AddWithValue("@expiresAt", apiKey.ExpiresAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@lastUsedAt", apiKey.LastUsedAt ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteApiKeyAsync(ApiKey apiKey)
    {
        const string sql = "DELETE FROM api_keys WHERE id = @id";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", apiKey.Id);

        await command.ExecuteNonQueryAsync();
    }

    private static ApiKey MapReaderToApiKey(NpgsqlDataReader reader)
    {
        return new ApiKey
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetString(1),
            Description = reader.GetString(2),
            TokenHash = reader.GetString(3),
            Prefix = reader.GetString(4),
            IsShared = reader.GetBoolean(5),
            CreatedAt = reader.GetDateTime(6),
            ExpiresAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            LastUsedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
        };
    }
}
