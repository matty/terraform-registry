using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
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
        var rows = new List<ModuleRow>();
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        var paramCounter = 0;

        var sql = @"
            SELECT 
                m.namespace,
                m.name,
                m.provider,
                m.version,
                m.description,
                m.published_at
            FROM 
                modules m
            WHERE m.deleted_at IS NULL";

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
        sql += " ORDER BY m.namespace, m.name, m.provider";

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
            var description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var publishedAt = reader.GetDateTime(5);

            rows.Add(new ModuleRow
            {
                Namespace = namespace_,
                Name = name,
                Version = version,
                Provider = provider,
                Description = description,
                PublishedAt = publishedAt.ToString("o")
            });
        }

        var modules = rows
            .GroupBy(row => new { row.Namespace, row.Name, row.Provider })
            .Select(group =>
            {
                var versions = group
                    .Select(row => row.Version)
                    .OrderByDescending(version => version, SemVerVersionComparer.Instance)
                    .ToList();
                var latest = group.First(row => row.Version == versions[0]);
                latest.Versions = versions;
                return latest;
            })
            .Where(row => string.IsNullOrWhiteSpace(request.Q)
                || row.Name.Contains(request.Q, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(request.Q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Namespace, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Provider, StringComparer.Ordinal)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(row => new ModuleListItem
            {
                Id = $"{row.Namespace}/{row.Name}/{row.Provider}",
                Owner = row.Namespace,
                Namespace = row.Namespace,
                Name = row.Name,
                Version = row.Version,
                Provider = row.Provider,
                Description = row.Description,
                PublishedAt = row.PublishedAt,
                Versions = row.Versions,
                DownloadUrl = $"{_baseUrl}/v1/modules/{row.Namespace}/{row.Name}/{row.Provider}/{row.Version}/download"
            })
            .ToList();

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
                dependencies::text,
                metadata::text
            FROM 
                modules m
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
        var metadata = DeserializeModuleMetadata(reader.GetString(8));
        var description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var publishedAt = reader.GetDateTime(6);
        await reader.DisposeAsync();

        var versions = await GetVersionsInternalAsync(connection, @namespace, name, provider);

        return new Module
        {
            Id = $"{@namespace}/{name}/{provider}/{version}",
            Owner = @namespace,
            Namespace = @namespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = description,
            Source = $"{_baseUrl}/{@namespace}/{name}",
            PublishedAt = publishedAt.ToString("o"),
            DownloadUrl = $"{_baseUrl}/v1/modules/{@namespace}/{name}/{provider}/{version}/download",
            Versions = versions,
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>
            {
                { provider, "*" }
            },
            Metadata = metadata
        };
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public async Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var versions = await GetVersionsInternalAsync(connection, @namespace, name, provider);

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
                dependencies::text,
                metadata::text
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
            Dependencies = dependencies,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
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
                dependencies,
                metadata
            )
            VALUES (
                @namespace,
                @name,
                @provider,
                @version,
                @description,
                @storagePath,
                @publishedAt,
                @dependencies,
                @metadata
            )";

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
            command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(module.Metadata)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;

            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogInformation("Module {Namespace}/{Name}/{Provider}/{Version} already exists in PostgreSQL",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
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

    public async Task<bool> RemoveModuleExactAsync(ModuleStorage module)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND description = @description
              AND storage_path = @storagePath
              AND published_at = @publishedAt
              AND dependencies = @dependencies
              AND deleted_at IS NULL";

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

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing exact module row {Namespace}/{Name}/{Provider}/{Version} from database",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    public async Task<bool> RemoveDeletedModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NOT NULL";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", @namespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error removing deleted module row {Namespace}/{Name}/{Provider}/{Version} from database",
                @namespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> AddDeletedModuleAsync(ModuleStorage module)
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
                dependencies,
                deleted_at
            )
            VALUES (
                @namespace,
                @name,
                @provider,
                @version,
                @description,
                @storagePath,
                @publishedAt,
                @dependencies,
                @deletedAt
            )";

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
            command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogInformation("Deleted module {Namespace}/{Name}/{Provider}/{Version} already exists in PostgreSQL",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error adding deleted module row {Namespace}/{Name}/{Provider}/{Version} to database",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    public async Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule)
    {
        var sql = @"
            UPDATE modules
            SET description = @newDescription,
                storage_path = @newStoragePath,
                published_at = @newPublishedAt,
                dependencies = @newDependencies
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND description = @description
              AND storage_path = @storagePath
              AND published_at = @publishedAt
              AND dependencies = @dependencies
              AND deleted_at IS NULL";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@namespace", existingModule.Namespace);
            command.Parameters.AddWithValue("@name", existingModule.Name);
            command.Parameters.AddWithValue("@provider", existingModule.Provider);
            command.Parameters.AddWithValue("@version", existingModule.Version);
            command.Parameters.AddWithValue("@description", existingModule.Description);
            command.Parameters.AddWithValue("@storagePath", existingModule.FilePath);
            command.Parameters.AddWithValue("@publishedAt", existingModule.PublishedAt);
            command.Parameters.AddWithValue("@dependencies",
                    existingModule.Dependencies == null ? "[]" : JsonSerializer.Serialize(existingModule.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;
            command.Parameters.AddWithValue("@newDescription", newModule.Description);
            command.Parameters.AddWithValue("@newStoragePath", newModule.FilePath);
            command.Parameters.AddWithValue("@newPublishedAt", newModule.PublishedAt);
            command.Parameters.AddWithValue("@newDependencies",
                    newModule.Dependencies == null ? "[]" : JsonSerializer.Serialize(newModule.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing exact module row {Namespace}/{Name}/{Provider}/{Version} in database",
                existingModule.Namespace, existingModule.Name, existingModule.Provider, existingModule.Version);
            return false;
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
        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies::text, metadata::text
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
            Dependencies = dependencies,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
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

    public async Task<ModuleExtractionDocument?> GetModuleExtractionAsync(string @namespace, string name,
        string provider, string version)
    {
        const string sql = @"
            SELECT e.document_json::text
            FROM module_extractions e
            JOIN modules m ON m.id = e.module_id
            WHERE m.namespace = @namespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        var json = (string?)await command.ExecuteScalarAsync();
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleExtractionDocument>(json);
    }

    public async Task UpsertModuleExtractionAsync(string @namespace, string name, string provider, string version,
        ModuleExtractionDocument document, string? sourceChecksum = null)
    {
        const string sql = @"
            INSERT INTO module_extractions (module_id, document_json, source_checksum, created_at, updated_at)
            SELECT id, @document, @checksum, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL
            ON CONFLICT (module_id) DO UPDATE SET
                document_json = EXCLUDED.document_json,
                source_checksum = EXCLUDED.source_checksum,
                updated_at = CURRENT_TIMESTAMP";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@document", JsonSerializer.Serialize(document)).NpgsqlDbType = NpgsqlDbType.Jsonb;
        command.Parameters.AddWithValue("@checksum", (object?)sourceChecksum ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<ModuleLlmContextDocument?> GetModuleLlmContextAsync(string @namespace, string name,
        string provider, string version)
    {
        const string sql = @"
            SELECT c.document_json::text
            FROM module_llm_contexts c
            JOIN modules m ON m.id = c.module_id
            WHERE m.namespace = @namespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        var json = (string?)await command.ExecuteScalarAsync();
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleLlmContextDocument>(json);
    }

    public async Task UpsertModuleLlmContextAsync(string @namespace, string name, string provider, string version,
        ModuleLlmContextDocument document, string? sourceChecksum = null)
    {
        const string sql = @"
            INSERT INTO module_llm_contexts (module_id, schema_version, generated_at, document_json, source_checksum, created_at, updated_at)
            SELECT id, @schemaVersion, @generatedAt, @document, @checksum, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL
            ON CONFLICT (module_id) DO UPDATE SET
                schema_version = EXCLUDED.schema_version,
                generated_at = EXCLUDED.generated_at,
                document_json = EXCLUDED.document_json,
                source_checksum = EXCLUDED.source_checksum,
                updated_at = CURRENT_TIMESTAMP";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@schemaVersion", document.SchemaVersion);
        command.Parameters.AddWithValue("@generatedAt", document.GeneratedAt);
        command.Parameters.AddWithValue("@document", JsonSerializer.Serialize(document)).NpgsqlDbType = NpgsqlDbType.Jsonb;
        command.Parameters.AddWithValue("@checksum", (object?)sourceChecksum ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateModuleMetadataAsync(string @namespace, string name, string provider, string version,
        Action<ModuleArtifactMetadata> mutate)
    {
        const string selectSql = @"
            SELECT metadata::text
            FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var selectCommand = new NpgsqlCommand(selectSql, connection);
        selectCommand.Parameters.AddWithValue("@namespace", @namespace);
        selectCommand.Parameters.AddWithValue("@name", name);
        selectCommand.Parameters.AddWithValue("@provider", provider);
        selectCommand.Parameters.AddWithValue("@version", version);

        var currentJson = (string?)await selectCommand.ExecuteScalarAsync();
        var metadata = DeserializeModuleMetadata(currentJson);
        mutate(metadata);

        const string updateSql = @"
            UPDATE modules
            SET metadata = @metadata
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NULL";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("@namespace", @namespace);
        updateCommand.Parameters.AddWithValue("@name", name);
        updateCommand.Parameters.AddWithValue("@provider", provider);
        updateCommand.Parameters.AddWithValue("@version", version);
        updateCommand.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(metadata)).NpgsqlDbType =
            NpgsqlDbType.Jsonb;

        await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies::text, m.metadata::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL AND e.module_id IS NULL
            ORDER BY m.published_at
            LIMIT @limit";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }

    public async Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync()
    {
        var summary = new ModuleExtractionAdminSummary();

        const string sql = @"
            SELECT m.metadata::text,
                   CASE WHEN e.module_id IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN c.module_id IS NULL THEN 0 ELSE 1 END
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            LEFT JOIN module_llm_contexts c ON c.module_id = m.id
            WHERE m.deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            summary.Total++;
            var metadata = DeserializeModuleMetadata(reader.IsDBNull(0) ? null : reader.GetString(0));
            IncrementStatus(summary, metadata.Extraction?.Status);
            IncrementLlmStatus(summary, metadata.LlmContext?.Status);

            if (Convert.ToInt32(reader.GetValue(1)) == 0)
                summary.NeverExtracted++;
            if (Convert.ToInt32(reader.GetValue(2)) == 0)
                summary.LlmNeverGenerated++;
        }

        return summary;
    }

    public async Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query)
    {
        var items = new List<ModuleExtractionAdminListItem>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata::text
            FROM modules m
            WHERE m.deleted_at IS NULL
            ORDER BY m.namespace, m.name, m.provider, m.version";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapModuleExtractionAdminListItem(reader));
        }

        var filtered = items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim();
            filtered = filtered.Where(item =>
                item.Namespace.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Version.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filtered = filtered.Where(item => string.Equals(
                item.Status,
                query.Status,
                StringComparison.OrdinalIgnoreCase));
        }

        var filteredItems = filtered.ToList();
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, 100);

        return new ModuleExtractionAdminPage
        {
            Total = filteredItems.Count,
            Items = filteredItems.Skip(offset).Take(limit).ToList()
        };
    }

    public async Task<ModuleExtractionAdminDetail?> GetModuleExtractionAdminDetailAsync(string @namespace, string name,
        string provider, string version)
    {
        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata::text, e.document_json::text, c.document_json::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            LEFT JOIN module_llm_contexts c ON c.module_id = m.id
            WHERE m.namespace = @namespace
              AND m.name = @name
              AND m.provider = @provider
              AND m.version = @version
              AND m.deleted_at IS NULL";

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

        var item = MapModuleExtractionAdminListItem(reader);
        var documentJson = reader.IsDBNull(6) ? null : reader.GetString(6);
        var llmContextJson = reader.IsDBNull(7) ? null : reader.GetString(7);

        return new ModuleExtractionAdminDetail
        {
            Namespace = item.Namespace,
            Name = item.Name,
            Provider = item.Provider,
            Version = item.Version,
            Description = item.Description,
            Status = item.Status,
            LastAttemptedAt = item.LastAttemptedAt,
            LastSucceededAt = item.LastSucceededAt,
            Error = item.Error,
            LlmStatus = item.LlmStatus,
            LlmLastAttemptedAt = item.LlmLastAttemptedAt,
            LlmLastSucceededAt = item.LlmLastSucceededAt,
            LlmError = item.LlmError,
            Documentation = item.Documentation,
            Document = string.IsNullOrWhiteSpace(documentJson)
                ? null
                : JsonSerializer.Deserialize<ModuleExtractionDocument>(documentJson),
            LlmContext = string.IsNullOrWhiteSpace(llmContextJson)
                ? null
                : JsonSerializer.Deserialize<ModuleLlmContextDocument>(llmContextJson)
        };
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesForExtractionBackfillAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        const string sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies::text, m.metadata::text
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL
              AND (
                e.module_id IS NULL
                OR COALESCE(m.metadata->'Extraction'->>'Status', 'pending') = 'failed'
              )
            ORDER BY m.published_at
            LIMIT @limit";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }

    private static async Task<List<string>> GetVersionsInternalAsync(NpgsqlConnection connection, string @namespace,
        string name, string provider)
    {
        const string sql = @"
            SELECT version
            FROM modules
            WHERE namespace = @namespace
              AND name = @name
              AND provider = @provider
              AND deleted_at IS NULL";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@namespace", @namespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);

        var versions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) versions.Add(reader.GetString(0));

        return versions.OrderByDescending(version => version, SemVerVersionComparer.Instance).ToList();
    }

    private static ModuleArtifactMetadata DeserializeModuleMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ModuleArtifactMetadata();

        return JsonSerializer.Deserialize<ModuleArtifactMetadata>(json) ?? new ModuleArtifactMetadata();
    }

    private static ModuleExtractionAdminListItem MapModuleExtractionAdminListItem(NpgsqlDataReader reader)
    {
        var metadata = DeserializeModuleMetadata(reader.IsDBNull(5) ? null : reader.GetString(5));
        return new ModuleExtractionAdminListItem
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            Status = metadata.Extraction?.Status ?? "pending",
            LastAttemptedAt = metadata.Extraction?.LastAttemptedAt,
            LastSucceededAt = metadata.Extraction?.LastSucceededAt,
            Error = metadata.Extraction?.Error,
            LlmStatus = metadata.LlmContext?.Status ?? "pending",
            LlmLastAttemptedAt = metadata.LlmContext?.LastAttemptedAt,
            LlmLastSucceededAt = metadata.LlmContext?.LastSucceededAt,
            LlmError = metadata.LlmContext?.Error,
            Documentation = metadata.Documentation
        };
    }

    private static void IncrementStatus(ModuleExtractionAdminSummary summary, string? status)
    {
        switch (status)
        {
            case "succeeded":
                summary.Succeeded++;
                break;
            case "failed":
                summary.Failed++;
                break;
            case "processing":
                summary.Processing++;
                break;
            default:
                summary.Pending++;
                break;
        }
    }

    private static void IncrementLlmStatus(ModuleExtractionAdminSummary summary, string? status)
    {
        switch (status)
        {
            case "succeeded":
                summary.LlmSucceeded++;
                break;
            case "failed":
                summary.LlmFailed++;
                break;
            case "processing":
                summary.LlmProcessing++;
                break;
            default:
                summary.LlmPending++;
                break;
        }
    }

    private static ModuleStorage MapModuleStorage(NpgsqlDataReader reader)
    {
        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = reader.GetDateTime(6),
            Dependencies = dependencies,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    private sealed class ModuleRow
    {
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required string Provider { get; init; }
        public required string Version { get; init; }
        public required string Description { get; init; }
        public required string PublishedAt { get; init; }
        public List<string> Versions { get; set; } = [];
    }

    // User Methods
    public async Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email)
    {
        const string sql =
            """
            SELECT id, email, provider, provider_id, created_at, updated_at
            FROM users
            WHERE lower(email) = lower(@email)
            ORDER BY CASE WHEN email = @email THEN 0 ELSE 1 END, created_at ASC
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
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
        await using var connection = new NpgsqlConnection(_connectionString);
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
            users.Add(MapUser(reader));
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
