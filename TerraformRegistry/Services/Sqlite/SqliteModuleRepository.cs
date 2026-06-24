using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteModuleRepository(
    string connectionString,
    string baseUrl,
    ILogger logger) : IModuleRepository
{
    public async Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        var rows = new List<ModuleRow>();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.published_at
            FROM modules m
            WHERE m.deleted_at IS NULL";

        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            conditions.Add(" AND m.namespace = $ns");
            parameters.Add(new SqliteParameter("$ns", request.Namespace));
        }

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            conditions.Add(" AND m.provider = $prov");
            parameters.Add(new SqliteParameter("$prov", request.Provider));
        }

        sql += string.Join("", conditions);
        sql += " ORDER BY m.namespace, m.name, m.provider";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ns = reader.GetString(0);
            var name = reader.GetString(1);
            var provider = reader.GetString(2);
            var version = reader.GetString(3);
            var description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var publishedAtIso = reader.GetString(5);

            rows.Add(new ModuleRow
            {
                Namespace = ns,
                Name = name,
                Version = version,
                Provider = provider,
                Description = description,
                PublishedAt = publishedAtIso
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

    public async Task<TerraformModule?> GetModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", moduleNamespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var publishedAtIso = reader.GetString(6);
        var versions = await GetVersionsInternal(connection, moduleNamespace, name, provider);

        return new TerraformModule
        {
            Id = $"{moduleNamespace}/{name}/{provider}/{version}",
            Owner = moduleNamespace,
            Namespace = moduleNamespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            Source = $"{baseUrl}/{moduleNamespace}/{name}",
            PublishedAt = publishedAtIso,
            DownloadUrl = $"{baseUrl}/v1/modules/{moduleNamespace}/{name}/{provider}/{version}/download",
            Versions = versions,
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>(StringComparer.Ordinal) { { provider, "*" } },
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<ModuleVersions> GetModuleVersionsAsync(string moduleNamespace, string name, string provider)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var versions = await GetVersionsInternal(connection, moduleNamespace, name, provider);
        return new ModuleVersions
        {
            Modules = new List<ModuleVersionInfo>
            {
                new()
                {
                    Versions = versions.Select(v => new VersionInfo { Version = v }).ToList()
                }
            }
        };
    }

    public async Task<ModuleStorage?> GetModuleStorageAsync(string moduleNamespace, string name, string provider,
        string version)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", moduleNamespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var depsJson = reader.GetString(7);
        var deps = string.IsNullOrWhiteSpace(depsJson)
            ? new List<string>()
            : (JsonSerializer.Deserialize<List<string>>(depsJson) ?? new List<string>());

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = ParseStoredDateTime(reader.GetString(6)),
            Dependencies = deps,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<bool> AddModuleAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            ) VALUES (
                $ns, $name, $prov, $ver, $desc, $path, $published, $deps, $metadata
            )";

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleStorage.Namespace);
            cmd.Parameters.AddWithValue("$name", moduleStorage.Name);
            cmd.Parameters.AddWithValue("$prov", moduleStorage.Provider);
            cmd.Parameters.AddWithValue("$ver", moduleStorage.Version);
            cmd.Parameters.AddWithValue("$desc", moduleStorage.Description);
            cmd.Parameters.AddWithValue("$path", moduleStorage.FilePath);
            cmd.Parameters.AddWithValue("$published", moduleStorage.PublishedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$deps",
                moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies));
            cmd.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(moduleStorage.Metadata));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.SqliteExtendedErrorCode == 2067)
        {
            RegistryLog.Information(logger, "Module {Namespace}/{Name}/{Provider}/{Version} already exists in SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Error adding module {Namespace}/{Name}/{Provider}/{Version} to SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    public async Task<bool> RemoveModuleAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleStorage.Namespace);
            cmd.Parameters.AddWithValue("$name", moduleStorage.Name);
            cmd.Parameters.AddWithValue("$prov", moduleStorage.Provider);
            cmd.Parameters.AddWithValue("$ver", moduleStorage.Version);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Error removing module {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    public async Task<bool> RemoveModuleExactAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = $ns
              AND name = $name
              AND provider = $prov
              AND version = $ver
              AND description = $desc
              AND storage_path = $path
              AND published_at = $published
              AND dependencies = $deps
              AND deleted_at IS NULL";

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleStorage.Namespace);
            cmd.Parameters.AddWithValue("$name", moduleStorage.Name);
            cmd.Parameters.AddWithValue("$prov", moduleStorage.Provider);
            cmd.Parameters.AddWithValue("$ver", moduleStorage.Version);
            cmd.Parameters.AddWithValue("$desc", moduleStorage.Description);
            cmd.Parameters.AddWithValue("$path", moduleStorage.FilePath);
            cmd.Parameters.AddWithValue("$published", moduleStorage.PublishedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$deps",
                moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies));
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex,
                "Error removing exact module row {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    public async Task<bool> RemoveDeletedModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = $ns
              AND name = $name
              AND provider = $prov
              AND version = $ver
              AND deleted_at IS NOT NULL";

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleNamespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$ver", version);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex,
                "Error removing deleted module row {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                moduleNamespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> AddDeletedModuleAsync(ModuleStorage moduleStorage)
    {
        var sql = @"
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies, deleted_at
            ) VALUES (
                $ns, $name, $prov, $ver, $desc, $path, $published, $deps, $deletedAt
            )";

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleStorage.Namespace);
            cmd.Parameters.AddWithValue("$name", moduleStorage.Name);
            cmd.Parameters.AddWithValue("$prov", moduleStorage.Provider);
            cmd.Parameters.AddWithValue("$ver", moduleStorage.Version);
            cmd.Parameters.AddWithValue("$desc", moduleStorage.Description);
            cmd.Parameters.AddWithValue("$path", moduleStorage.FilePath);
            cmd.Parameters.AddWithValue("$published", moduleStorage.PublishedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$deps",
                moduleStorage.Dependencies == null ? "[]" : JsonSerializer.Serialize(moduleStorage.Dependencies));
            cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.SqliteExtendedErrorCode == 2067)
        {
            RegistryLog.Information(logger, "Deleted module {Namespace}/{Name}/{Provider}/{Version} already exists in SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex,
                "Error adding deleted module row {Namespace}/{Name}/{Provider}/{Version} to SQLite",
                moduleStorage.Namespace, moduleStorage.Name, moduleStorage.Provider, moduleStorage.Version);
            return false;
        }
    }

    public async Task<bool> ReplaceModuleExactAsync(ModuleStorage existingModule, ModuleStorage newModule)
    {
        var sql = @"
            UPDATE modules
            SET description = $newDesc,
                storage_path = $newPath,
                published_at = $newPublished,
                dependencies = $newDeps
            WHERE namespace = $ns
              AND name = $name
              AND provider = $prov
              AND version = $ver
              AND description = $desc
              AND storage_path = $path
              AND published_at = $published
              AND dependencies = $deps
              AND deleted_at IS NULL";

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", existingModule.Namespace);
            cmd.Parameters.AddWithValue("$name", existingModule.Name);
            cmd.Parameters.AddWithValue("$prov", existingModule.Provider);
            cmd.Parameters.AddWithValue("$ver", existingModule.Version);
            cmd.Parameters.AddWithValue("$desc", existingModule.Description);
            cmd.Parameters.AddWithValue("$path", existingModule.FilePath);
            cmd.Parameters.AddWithValue("$published", existingModule.PublishedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$deps",
                existingModule.Dependencies == null ? "[]" : JsonSerializer.Serialize(existingModule.Dependencies));
            cmd.Parameters.AddWithValue("$newDesc", newModule.Description);
            cmd.Parameters.AddWithValue("$newPath", newModule.FilePath);
            cmd.Parameters.AddWithValue("$newPublished", newModule.PublishedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$newDeps",
                newModule.Dependencies == null ? "[]" : JsonSerializer.Serialize(newModule.Dependencies));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex,
                "Error replacing exact module row {Namespace}/{Name}/{Provider}/{Version} in SQLite",
                existingModule.Namespace, existingModule.Name, existingModule.Provider, existingModule.Version);
            return false;
        }
    }

    public async Task<bool> SoftDeleteModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = $deletedAt 
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleNamespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$ver", version);
            cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Error soft deleting module {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                moduleNamespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> RestoreModuleAsync(string moduleNamespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = NULL 
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NOT NULL";
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleNamespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$ver", version);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Error restoring module {Namespace}/{Name}/{Provider}/{Version} in SQLite",
                moduleNamespace, name, provider, version);
            return false;
        }
    }

    public async Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request)
    {
        var modules = new List<ModuleListItem>();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT namespace, name, provider, version, description, published_at
            FROM modules WHERE deleted_at IS NOT NULL";

        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            conditions.Add(" AND (lower(name) LIKE lower($q) OR lower(description) LIKE lower($q))");
            parameters.Add(new SqliteParameter("$q", $"%{request.Q}%"));
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            conditions.Add(" AND namespace = $ns");
            parameters.Add(new SqliteParameter("$ns", request.Namespace));
        }

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            conditions.Add(" AND provider = $prov");
            parameters.Add(new SqliteParameter("$prov", request.Provider));
        }

        sql += string.Join("", conditions);
        sql += " ORDER BY namespace, name, provider, version LIMIT $limit OFFSET $offset";
        parameters.Add(new SqliteParameter("$limit", request.Limit));
        parameters.Add(new SqliteParameter("$offset", request.Offset));

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.Add(p);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(new ModuleListItem
            {
                Id = $"{reader.GetString(0)}/{reader.GetString(1)}/{reader.GetString(2)}/{reader.GetString(3)}",
                Owner = reader.GetString(0),
                Namespace = reader.GetString(0),
                Name = reader.GetString(1),
                Version = reader.GetString(3),
                Provider = reader.GetString(2),
                Description = reader.GetString(4),
                PublishedAt = reader.GetString(5),
                Versions = new List<string> { reader.GetString(3) },
                DownloadUrl =
                    $"{baseUrl}/v1/modules/{reader.GetString(0)}/{reader.GetString(1)}/{reader.GetString(2)}/{reader.GetString(3)}/download"
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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", moduleNamespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var depsJson = reader.GetString(7);
        var deps = string.IsNullOrWhiteSpace(depsJson)
            ? new List<string>()
            : (JsonSerializer.Deserialize<List<string>>(depsJson) ?? new List<string>());

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = ParseStoredDateTime(reader.GetString(6)),
            Dependencies = deps,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<bool> UpdateModuleDescriptionAsync(string moduleNamespace, string name, string provider,
        string description)
    {
        var sql = @"UPDATE modules SET description = $desc
            WHERE namespace = $ns AND name = $name AND provider = $prov AND deleted_at IS NULL";
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", moduleNamespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$desc", description);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Error updating description for module {Namespace}/{Name}/{Provider} in SQLite",
                moduleNamespace, name, provider);
            return false;
        }
    }
    private static async Task<List<string>> GetVersionsInternal(SqliteConnection connection, string moduleNamespace,
        string name, string provider)
    {
        var versions = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT version FROM modules WHERE namespace = $ns AND name = $name AND provider = $prov AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$ns", moduleNamespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) versions.Add(r.GetString(0));
        return versions.OrderByDescending(version => version, SemVerVersionComparer.Instance).ToList();
    }

    private static ModuleArtifactMetadata DeserializeModuleMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ModuleArtifactMetadata();

        return JsonSerializer.Deserialize<ModuleArtifactMetadata>(json) ?? new ModuleArtifactMetadata();
    }
    private static ModuleStorage MapModuleStorage(SqliteDataReader reader)
    {
        var depsJson = reader.GetString(7);
        var deps = string.IsNullOrWhiteSpace(depsJson)
            ? new List<string>()
            : (JsonSerializer.Deserialize<List<string>>(depsJson) ?? new List<string>());

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            FilePath = reader.GetString(5),
            PublishedAt = ParseStoredDateTime(reader.GetString(6)),
            Dependencies = deps,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    private static DateTime ParseStoredDateTime(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
