using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModuleRepository(
    string connectionString,
    string baseUrl,
    ILogger logger) : IModuleRepository
{
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
        }

        sql += string.Join(" ", conditions);
        sql += " ORDER BY m.namespace, m.name, m.provider";

        await using var connection = new NpgsqlConnection(connectionString);
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
                PublishedAt = publishedAt.ToString("o", CultureInfo.InvariantCulture)
            });
        }

        var listedRows = rows
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
            .ToList();

        var total = listedRows.Count;

        var modules = listedRows
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
                DownloadUrl = $"{baseUrl}/v1/modules/{row.Namespace}/{row.Name}/{row.Provider}/{row.Version}/download"
            })
            .ToList();

        return new ModuleList
        {
            Modules = modules,
            Meta = new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "limit", request.Limit.ToString(CultureInfo.InvariantCulture) },
                { "current_offset", request.Offset.ToString(CultureInfo.InvariantCulture) },
                { "total", total.ToString(CultureInfo.InvariantCulture) }
            }
        };
    }

    /// <summary>
    ///     Gets detailed information about a specific module
    /// </summary>
    public async Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version)
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
                namespace = @moduleNamespace AND
                name = @name AND
                provider = @provider AND
                version = @version AND
                deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var metadata = DeserializeModuleMetadata(reader.GetString(8));
        var description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var publishedAt = reader.GetDateTime(6);
        await reader.DisposeAsync();

        var versions = await GetVersionsInternalAsync(connection, moduleNamespace, name, provider);

        return new TerraformModule
        {
            Id = $"{moduleNamespace}/{name}/{provider}/{version}",
            Owner = moduleNamespace,
            Namespace = moduleNamespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = description,
            Source = $"{baseUrl}/{moduleNamespace}/{name}",
            PublishedAt = publishedAt.ToString("o", CultureInfo.InvariantCulture),
            DownloadUrl = $"{baseUrl}/v1/modules/{moduleNamespace}/{name}/{provider}/{version}/download",
            Versions = versions,
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { provider, "*" }
            },
            Metadata = metadata
        };
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public async Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var versions = await GetVersionsInternalAsync(connection, moduleNamespace, name, provider);

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
    public async Task<ModuleStorage?> GetModuleStorageAsync(string moduleNamespace, string name, string provider,
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
                namespace = @moduleNamespace AND
                name = @name AND
                provider = @provider AND
                version = @version AND
                deleted_at IS NULL";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
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
    public async Task<bool> AddModuleAsync(ModuleStorage moduleStorage)
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
                @moduleNamespace,
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
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleStorage.Namespace);
            command.Parameters.AddWithValue("@name", moduleStorage.Name);
            command.Parameters.AddWithValue("@provider", moduleStorage.Provider);
            command.Parameters.AddWithValue("@version", moduleStorage.Version);
            command.Parameters.AddWithValue("@description", moduleStorage.Description);
            command.Parameters.AddWithValue("@storagePath", moduleStorage.FilePath);
            command.Parameters.AddWithValue("@publishedAt", moduleStorage.PublishedAt);
            command.Parameters.AddWithValue("@dependencies",
                    moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;
            command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(moduleStorage.Metadata)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;

            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            RegistryLog.Information(logger, "Module {Namespace}/{Name}/{Provider}/{Version} already exists in PostgreSQL",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error adding module {Namespace}/{Name}/{Provider}/{Version} to database",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    /// <summary>
    ///     Removes a module from the database
    /// </summary>
    public async Task<bool> RemoveModuleAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version";

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleStorage.Namespace);
            command.Parameters.AddWithValue("@name", moduleStorage.Name);
            command.Parameters.AddWithValue("@provider", moduleStorage.Provider);
            command.Parameters.AddWithValue("@version", moduleStorage.Version);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // FK violation
        {
            RegistryLog.Error(logger, ex,
                "Cannot remove module {Namespace}/{Name}/{Provider}/{Version}: referenced by download records",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error removing module {Namespace}/{Name}/{Provider}/{Version} from database",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            throw;
        }
    }

    public async Task<bool> RemoveModuleExactAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @moduleNamespace
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
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleStorage.Namespace);
            command.Parameters.AddWithValue("@name", moduleStorage.Name);
            command.Parameters.AddWithValue("@provider", moduleStorage.Provider);
            command.Parameters.AddWithValue("@version", moduleStorage.Version);
            command.Parameters.AddWithValue("@description", moduleStorage.Description);
            command.Parameters.AddWithValue("@storagePath", moduleStorage.FilePath);
            command.Parameters.AddWithValue("@publishedAt", moduleStorage.PublishedAt);
            command.Parameters.AddWithValue("@dependencies",
                    moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error removing exact module row {Namespace}/{Name}/{Provider}/{Version} from database",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    public async Task<bool> RemoveDeletedModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND version = @version
              AND deleted_at IS NOT NULL";

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex,
                "Error removing deleted module row {Namespace}/{Name}/{Provider}/{Version} from database",
                moduleNamespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> AddDeletedModuleAsync(ModuleStorage moduleStorage)
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
                @moduleNamespace,
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
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleStorage.Namespace);
            command.Parameters.AddWithValue("@name", moduleStorage.Name);
            command.Parameters.AddWithValue("@provider", moduleStorage.Provider);
            command.Parameters.AddWithValue("@version", moduleStorage.Version);
            command.Parameters.AddWithValue("@description", moduleStorage.Description);
            command.Parameters.AddWithValue("@storagePath", moduleStorage.FilePath);
            command.Parameters.AddWithValue("@publishedAt", moduleStorage.PublishedAt);
            command.Parameters.AddWithValue("@dependencies",
                    moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies)).NpgsqlDbType =
                NpgsqlDbType.Jsonb;
            command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            RegistryLog.Information(logger, "Deleted module {Namespace}/{Name}/{Provider}/{Version} already exists in PostgreSQL",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex,
                "Error adding deleted module row {Namespace}/{Name}/{Provider}/{Version} to database",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
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
            WHERE namespace = @moduleNamespace
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
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", existingModule.Namespace);
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
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error replacing exact module row {Namespace}/{Name}/{Provider}/{Version} in database",
                existingModule.Namespace, existingModule.Name, existingModule.Provider, existingModule.Version);
            return false;
        }
    }

    public async Task<bool> SoftDeleteModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = @deletedAt 
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider AND version = @version AND deleted_at IS NULL";
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);
            command.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error soft deleting module {Namespace}/{Name}/{Provider}/{Version} from PostgreSQL",
                moduleNamespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> RestoreModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = NULL 
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider AND version = @version AND deleted_at IS NOT NULL";
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@version", version);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error restoring module {Namespace}/{Name}/{Provider}/{Version} in PostgreSQL",
                moduleNamespace, name, provider, version);
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

        await using var connection = new NpgsqlConnection(connectionString);
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
                PublishedAt = reader.GetDateTime(6).ToString("o", CultureInfo.InvariantCulture),
                Versions = new List<string> { v },
                DownloadUrl = $"{baseUrl}/v1/modules/{ns}/{n}/{p}/{v}/download"
            });
        }

        return new ModuleList
        {
            Modules = modules,
            Meta = new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "limit", request.Limit.ToString(CultureInfo.InvariantCulture) },
                { "current_offset", request.Offset.ToString(CultureInfo.InvariantCulture) }
            }
        };
    }

    public async Task<ModuleStorage?> GetModuleStorageIncludingDeletedAsync(string moduleNamespace, string name,
        string provider, string version)
    {
        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies::text, metadata::text
            FROM modules WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider AND version = @version";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
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

    public async Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider,
        string description)
    {
        var sql = @"UPDATE modules SET description = @description
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider AND deleted_at IS NULL";
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@provider", provider);
            command.Parameters.AddWithValue("@description", description);
            var rows = await command.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Error(logger, ex, "Error updating description for module {Namespace}/{Name}/{Provider} in PostgreSQL",
                moduleNamespace, name, provider);
            return false;
        }
    }
    private static async Task<List<string>> GetVersionsInternalAsync(NpgsqlConnection connection, string moduleNamespace,
        string name, string provider)
    {
        const string sql = @"
            SELECT version
            FROM modules
            WHERE namespace = @moduleNamespace
              AND name = @name
              AND provider = @provider
              AND deleted_at IS NULL";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
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

}
