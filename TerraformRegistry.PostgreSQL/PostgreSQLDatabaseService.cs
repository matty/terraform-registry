using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Migrations;

namespace TerraformRegistry.PostgreSQL;

/// <summary>
///     Implementation of a database service using PostgreSQL
/// </summary>
public class PostgreSqlDatabaseService : IDatabaseService, IInitializableDb
{
    private readonly string _baseUrl;
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlDatabaseService> _logger;
    private readonly DbUpMigrator _dbUpMigrator;

    public PostgreSqlDatabaseService(string connectionString, string baseUrl, ILogger<PostgreSqlDatabaseService> logger,
        DbUpMigrator dbUpMigrator)
    {
        _connectionString = connectionString;
        _baseUrl = baseUrl;
        _dbUpMigrator = dbUpMigrator;
        _logger = logger;
    }

    /// <summary>
    ///     Lists all modules based on search criteria
    /// </summary>
    public async Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        var modules = new List<ModuleListItem>();
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        var paramCounter = 0;

        var sql = @"
            WITH latest_versions AS (
                SELECT 
                    namespace,
                    name,
                    provider,
                    MAX(version) AS latest_version
                FROM 
                    modules
                WHERE deleted_at IS NULL
                GROUP BY 
                    namespace, name, provider
            )
            SELECT 
                m.namespace,
                m.name,
                m.provider,
                m.version,
                m.description,
                m.storage_path,
                m.published_at,
                ARRAY(
                    SELECT version 
                    FROM modules 
                    WHERE 
                        namespace = m.namespace AND 
                        name = m.name AND 
                        provider = m.provider AND
                        deleted_at IS NULL
                    ORDER BY version DESC
                ) AS versions
            FROM 
                modules m
            INNER JOIN 
                latest_versions lv ON 
                    m.namespace = lv.namespace AND 
                    m.name = lv.name AND 
                    m.provider = lv.provider AND 
                    m.version = lv.latest_version
            WHERE m.deleted_at IS NULL";

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            conditions.Add($" AND (m.name ILIKE @p{paramCounter} OR m.description ILIKE @p{paramCounter})");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", $"%{request.Q}%"));
            paramCounter++;
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            conditions.Add($" AND m.namespace = @p{paramCounter}");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Namespace));
            paramCounter++;
        }

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            conditions.Add($" AND m.provider = @p{paramCounter}");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Provider));
            paramCounter++;
        }

        sql += string.Join(" ", conditions);
        sql += $" ORDER BY m.namespace, m.name, m.provider LIMIT @p{paramCounter} OFFSET @p{paramCounter + 1}";
        parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Limit));
        parameters.Add(new NpgsqlParameter($"@p{paramCounter + 1}", request.Offset));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var namespace_ = reader.GetString(0);
            var name = reader.GetString(1);
            var provider = reader.GetString(2);
            var version = reader.GetString(3);
            var description = reader.GetString(4);
            var publishedAt = reader.GetDateTime(6);
            var versions = reader.GetFieldValue<string[]>(7);

            modules.Add(new ModuleListItem
            {
                Id = $"{namespace_}/{name}/{provider}",
                Owner = namespace_,
                Namespace = namespace_,
                Name = name,
                Version = version,
                Provider = provider,
                Description = description,
                PublishedAt = publishedAt.ToString("o"),
                Versions = versions.ToList(),
                DownloadUrl = $"{_baseUrl}/v1/modules/{namespace_}/{name}/{provider}/{version}/download"
            });
        }

        return new ModuleList
        {
            Modules = modules,
            Meta = new Dictionary<string, string>
            {
                { "limit", request.Limit.ToString() },
                { "current_offset", request.Offset.ToString() }
            }
        };
    }

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    public async Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"
            SELECT 
                namespace,
                name,
                provider,
                version,
                description,
                storage_path,
                published_at,
                dependencies,
                (
                    SELECT 
                        ARRAY(
                            SELECT version 
                            FROM modules 
                            WHERE 
                                namespace = m.namespace AND 
                                name = m.name AND 
                                provider = m.provider
                            ORDER BY version DESC
                        )
                ) AS versions
            FROM 
                modules m
            WHERE 
                namespace = @namespace AND
                name = @name AND
                provider = @provider AND
                version = @version";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();
        var versions = reader.GetFieldValue<string[]>(8);

        return new Module
        {
            Id = $"{@namespace}/{name}/{provider}/{version}",
            Owner = @namespace,
            Namespace = @namespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = reader.GetString(4),
            Source = $"{_baseUrl}/{@namespace}/{name}",
            PublishedAt = reader.GetDateTime(6).ToString("o"),
            DownloadUrl = $"{_baseUrl}/v1/modules/{@namespace}/{name}/{provider}/{version}/download",
            Versions = versions.ToList(),
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>
            {
                { provider, "*" }
            }
        };
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public async Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        var sql = @"
            SELECT 
                version
            FROM 
                modules
            WHERE 
                namespace = @namespace AND
                name = @name AND
                provider = @provider AND
                deleted_at IS NULL
            ORDER BY 
                version DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);

        var versions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) versions.Add(reader.GetString(0));

        return new ModuleVersions
        {
            Modules = new List<ModuleVersionInfo>
            {
                new ModuleVersionInfo
                {
                    Versions = versions.Select(v => new VersionInfo { Version = v }).ToList()
                }
            }
        };
    }

    /// <summary>
    ///     Gets the storage path information for a specific module version
    /// </summary>
    public async Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider,
        string version)
    {
        var sql = @"
            SELECT
                namespace,
                name,
                provider,
                version,
                description,
                storage_path,
                published_at,
                dependencies
            FROM
                modules
            WHERE
                namespace = @namespace AND
                name = @name AND
                provider = @provider AND
                version = @version AND
                deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = reader.GetDateTime(6),
            Dependencies = dependencies
        };
    }

    /// <summary>
    ///     Adds a new module to the database
    /// </summary>
    public async Task<bool> AddModuleAsync(ModuleStorage module)
    {
        var sql = @"
            INSERT INTO modules (
                namespace,
                name,
                provider,
                version,
                description,
                storage_path,
                published_at,
                dependencies
            )
            VALUES (
                @namespace,
                @name,
                @provider,
                @version,
                @description,
                @storagePath,
                @publishedAt,
                @dependencies
            )
            ON CONFLICT (namespace, name, provider, version) 
            DO UPDATE SET
                description = @description,
                storage_path = @storagePath,
                dependencies = @dependencies
            RETURNING id";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", module.Namespace);
            command.Parameters.AddWithValue("@name", module.Name);
            command.Parameters.AddWithValue("@provider", module.Provider);
            command.Parameters.AddWithValue("@version", module.Version);
            command.Parameters.AddWithValue("@description", module.Description);
            command.Parameters.AddWithValue("@storagePath", module.FilePath);
            command.Parameters.AddWithValue("@publishedAt", module.PublishedAt);
            command.Parameters.AddWithValue("@dependencies",
                    module.Dependencies == null ? "[]" : JsonSerializer.Serialize(module.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;

            var result = await command.ExecuteScalarAsync();
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding module {Namespace}/{Name}/{Provider}/{Version} to database",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    /// <summary>
    ///     Removes a module from the database
    /// </summary>
    public async Task<bool> RemoveModuleAsync(ModuleStorage module)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", module.Namespace);
            command.Parameters.AddWithValue("@name", module.Name);
            command.Parameters.AddWithValue("@provider", module.Provider);
            command.Parameters.AddWithValue("@version", module.Version);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // FK violation
        {
            _logger.LogError(ex,
                "Cannot remove module {Namespace}/{Name}/{Provider}/{Version}: referenced by download records",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing module {Namespace}/{Name}/{Provider}/{Version} from database",
                module.Namespace, module.Name, module.Provider, module.Version);
            throw;
        }
    }

    public async Task<bool> SoftDeleteModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = @deletedAt 
            WHERE namespace = @namespace AND name = @name AND provider = @provider AND version = @version AND deleted_at IS NULL";
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", @namespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);
            command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting module {Namespace}/{Name}/{Provider}/{Version} from PostgreSQL",
                @namespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> RestoreModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = NULL 
            WHERE namespace = @namespace AND name = @name AND provider = @provider AND version = @version AND deleted_at IS NOT NULL";
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", @namespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring module {Namespace}/{Name}/{Provider}/{Version} in PostgreSQL",
                @namespace, name, provider, version);
            return false;
        }
    }

    public async Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request)
    {
        var modules = new List<ModuleListItem>();
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        var paramCounter = 0;

        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at
            FROM modules WHERE deleted_at IS NOT NULL";

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            conditions.Add($" AND (name ILIKE @p{paramCounter} OR description ILIKE @p{paramCounter})");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", $"%{request.Q}%"));
            paramCounter++;
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            conditions.Add($" AND namespace = @p{paramCounter}");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Namespace));
            paramCounter++;
        }

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            conditions.Add($" AND provider = @p{paramCounter}");
            parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Provider));
            paramCounter++;
        }

        sql += string.Join(" ", conditions);
        sql += $" ORDER BY namespace, name, provider, version LIMIT @p{paramCounter} OFFSET @p{paramCounter + 1}";
        parameters.Add(new NpgsqlParameter($"@p{paramCounter}", request.Limit));
        parameters.Add(new NpgsqlParameter($"@p{paramCounter + 1}", request.Offset));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ns = reader.GetString(0);
            var n = reader.GetString(1);
            var p = reader.GetString(2);
            var v = reader.GetString(3);
            modules.Add(new ModuleListItem
            {
                Id = $"{ns}/{n}/{p}/{v}",
                Owner = ns,
                Namespace = ns,
                Name = n,
                Version = v,
                Provider = p,
                Description = reader.GetString(4),
                PublishedAt = reader.GetDateTime(6).ToString("o"),
                Versions = new List<string> { v },
                DownloadUrl = $"{_baseUrl}/v1/modules/{ns}/{n}/{p}/{v}/download"
            });
        }

        return new ModuleList
        {
            Modules = modules,
            Meta = new Dictionary<string, string>
            {
                { "limit", request.Limit.ToString() },
                { "current_offset", request.Offset.ToString() }
            }
        };
    }

    public async Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(string @namespace, string name,
        string provider, string version)
    {
        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies
            FROM modules WHERE namespace = @namespace AND name = @name AND provider = @provider AND version = @version";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = reader.GetDateTime(6),
            Dependencies = dependencies
        };
    }

    public async Task<bool> UpdateModuleDescriptionAsync(string @namespace, string name, string provider,
        string description)
    {
        var sql = @"UPDATE modules SET description = @description
            WHERE namespace = @namespace AND name = @name AND provider = @provider AND deleted_at IS NULL";
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", @namespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@description", description);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating description for module {Namespace}/{Name}/{Provider} in PostgreSQL",
                @namespace, name, provider);
            return false;
        }
    }

    // User Methods
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        const string sql =
            "SELECT id, email, provider, provider_id, created_at, updated_at FROM users WHERE email = @email";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@email", email);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

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

    public async Task<User?> GetUserByIdAsync(string id)
    {
        const string sql = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users WHERE id = @id";
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

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

    public async Task AddUserAsync(User user)
    {
        const string sql = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES (@id, @email, @provider, @providerId, @createdAt, @updatedAt)";

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", userId);
        await command.ExecuteNonQueryAsync();
    }

    // ApiKey Methods
    public async Task AddApiKeyAsync(ApiKey apiKey)
    {
        const string sql = @"
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at, expires_at, last_used_at)
            VALUES (@id, @userId, @description, @tokenHash, @prefix, @isShared, @createdAt, @expiresAt, @lastUsedAt)";

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", apiKey.Id);

        await command.ExecuteNonQueryAsync();
    }

    private ApiKey MapReaderToApiKey(NpgsqlDataReader reader)
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

    public async Task RecordDownloadAsync(string @namespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT record_module_download(@p0, @p1, @p2, @p3, @p4, @p5)", conn);
            cmd.Parameters.AddWithValue("@p0", @namespace);
            cmd.Parameters.AddWithValue("@p1", name);
            cmd.Parameters.AddWithValue("@p2", provider);
            cmd.Parameters.AddWithValue("@p3", version);
            cmd.Parameters.AddWithValue("@p4", (object?)clientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p5", (object?)userAgent ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record download for {Namespace}/{Name}/{Provider}/{Version}",
                @namespace, name, provider, version);
        }
    }

    public async Task<IEnumerable<User>> ListAllUsersAsync()
    {
        const string sql = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users ORDER BY created_at DESC";
        var users = new List<User>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetString(0),
                Email = reader.GetString(1),
                Provider = reader.GetString(2),
                ProviderId = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5)
            });
        }

        return users;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task InitializeDatabase()
    {
        _dbUpMigrator.Migrate("postgres", _connectionString);
        return Task.CompletedTask;
    }
}