using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteApiKeyRepository(string connectionString) : IApiKeyRepository
{
    public async Task AddApiKeyAsync(ApiKey apiKey)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at)
            VALUES ($id, $uid, $desc, $hash, $prefix, $shared, $created, $expires, $lastUsed)";

        cmd.Parameters.AddWithValue("$id", apiKey.Id.ToString());
        cmd.Parameters.AddWithValue("$uid", apiKey.UserId);
        cmd.Parameters.AddWithValue("$desc", apiKey.Description);
        cmd.Parameters.AddWithValue("$hash", apiKey.TokenHash);
        cmd.Parameters.AddWithValue("$prefix", apiKey.Prefix);
        cmd.Parameters.AddWithValue("$shared", apiKey.IsShared ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", apiKey.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$expires",
            apiKey.ExpiresAt.HasValue ? apiKey.ExpiresAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("$lastUsed",
            apiKey.LastUsedAt.HasValue ? apiKey.LastUsedAt.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ApiKey?> GetApiKeyAsync(Guid id)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapApiKey(reader);
    }

    public async Task<IEnumerable<ApiKey>> GetApiKeysByUserAsync(string userId)
    {
        var keys = new List<ApiKey>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE user_id = $uid ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("$uid", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapApiKey(reader));
        }

        return keys;
    }

    public async Task<IEnumerable<ApiKey>> GetSharedApiKeysAsync()
    {
        var keys = new List<ApiKey>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE is_shared = 1 ORDER BY created_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapApiKey(reader));
        }

        return keys;
    }

    public async Task<IEnumerable<ApiKey>> GetApiKeysByPrefixAsync(string prefix)
    {
        var keys = new List<ApiKey>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at FROM api_keys WHERE prefix = $prefix";
        cmd.Parameters.AddWithValue("$prefix", prefix);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(MapApiKey(reader));
        }

        return keys;
    }

    public async Task UpdateApiKeyAsync(ApiKey apiKey)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE api_keys
            SET description = $desc, is_shared = $shared, expires_at = $expiresAt, last_used_at = $lastUsed
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", apiKey.Id.ToString());
        cmd.Parameters.AddWithValue("$desc", apiKey.Description);
        cmd.Parameters.AddWithValue("$shared", apiKey.IsShared ? 1 : 0);
        cmd.Parameters.AddWithValue("$expiresAt",
            apiKey.ExpiresAt.HasValue ? apiKey.ExpiresAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("$lastUsed",
            apiKey.LastUsedAt.HasValue ? apiKey.LastUsedAt.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteApiKeyAsync(ApiKey apiKey)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM api_keys WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", apiKey.Id.ToString());

        await cmd.ExecuteNonQueryAsync();
    }
    private static ApiKey MapApiKey(SqliteDataReader reader)
    {
        return new ApiKey
        {
            Id = Guid.Parse(reader.GetString(0)),
            UserId = reader.GetString(1),
            Description = reader.GetString(2),
            TokenHash = reader.GetString(3),
            Prefix = reader.GetString(4),
            IsShared = reader.GetInt32(5) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            ExpiresAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
            LastUsedAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8))
        };
    }
}
