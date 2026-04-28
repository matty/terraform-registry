using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
///     Lightweight SQLite implementation for local development/storage.
///     Stores dependencies as JSON TEXT and published_at as ISO-8601 TEXT.
/// </summary>
public class SqliteDatabaseService : IDatabaseService, IInitializableDb
{
    private readonly string _connectionString;
    private readonly string _baseUrl;
    private readonly ILogger<SqliteDatabaseService> _logger;
    private readonly DbUpMigrator _dbUpMigrator;

    public SqliteDatabaseService(string connectionString, string baseUrl, ILogger<SqliteDatabaseService> logger,
        DbUpMigrator dbUpMigrator)
    {
        _connectionString = connectionString;
        _baseUrl = baseUrl;
        _logger = logger;
        _dbUpMigrator = dbUpMigrator;
    }

    public Task InitializeDatabase()
    {
        _dbUpMigrator.Migrate("sqlite", _connectionString);
        return Task.CompletedTask;
    }

    public async Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        var rows = new List<ModuleRow>();

        await using var connection = new SqliteConnection(_connectionString);
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

    public async Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var publishedAtIso = reader.GetString(6);
        var versions = await GetVersionsInternal(connection, @namespace, name, provider);

        return new Module
        {
            Id = $"{@namespace}/{name}/{provider}/{version}",
            Owner = @namespace,
            Namespace = @namespace,
            Name = name,
            Version = version,
            Provider = provider,
            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            Source = $"{_baseUrl}/{@namespace}/{name}",
            PublishedAt = publishedAtIso,
            DownloadUrl = $"{_baseUrl}/v1/modules/{@namespace}/{name}/{provider}/{version}/download",
            Versions = versions,
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string> { { provider, "*" } },
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<ModuleVersions> GetModuleVersionsAsync(string @namespace, string name, string provider)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var versions = await GetVersionsInternal(connection, @namespace, name, provider);
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

    public async Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider,
        string version)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", @namespace);
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
            PublishedAt = DateTime.Parse(reader.GetString(6)),
            Dependencies = deps,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<bool> AddModuleAsync(ModuleStorage module)
    {
        var sql = @"
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            ) VALUES (
                $ns, $name, $prov, $ver, $desc, $path, $published, $deps, $metadata
            )";

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", module.Namespace);
            cmd.Parameters.AddWithValue("$name", module.Name);
            cmd.Parameters.AddWithValue("$prov", module.Provider);
            cmd.Parameters.AddWithValue("$ver", module.Version);
            cmd.Parameters.AddWithValue("$desc", module.Description);
            cmd.Parameters.AddWithValue("$path", module.FilePath);
            cmd.Parameters.AddWithValue("$published", module.PublishedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$deps",
                module.Dependencies == null ? "[]" : JsonSerializer.Serialize(module.Dependencies));
            cmd.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(module.Metadata));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.SqliteExtendedErrorCode == 2067)
        {
            _logger.LogInformation("Module {Namespace}/{Name}/{Provider}/{Version} already exists in SQLite",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding module {Namespace}/{Name}/{Provider}/{Version} to SQLite",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    public async Task<bool> RemoveModuleAsync(ModuleStorage module)
    {
        var sql = @"
            DELETE FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", module.Namespace);
            cmd.Parameters.AddWithValue("$name", module.Name);
            cmd.Parameters.AddWithValue("$prov", module.Provider);
            cmd.Parameters.AddWithValue("$ver", module.Version);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing module {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    public async Task<bool> SoftDeleteModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = $deletedAt 
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", @namespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$ver", version);
            cmd.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("o"));
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting module {Namespace}/{Name}/{Provider}/{Version} from SQLite",
                @namespace, name, provider, version);
            return false;
        }
    }

    public async Task<bool> RestoreModuleAsync(string @namespace, string name, string provider, string version)
    {
        var sql = @"UPDATE modules SET deleted_at = NULL 
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NOT NULL";
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", @namespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$ver", version);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring module {Namespace}/{Name}/{Provider}/{Version} in SQLite",
                @namespace, name, provider, version);
            return false;
        }
    }

    public async Task<ModuleList> ListDeletedModulesAsync(ModuleSearchRequest request)
    {
        var modules = new List<ModuleListItem>();

        await using var connection = new SqliteConnection(_connectionString);
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
                    $"{_baseUrl}/v1/modules/{reader.GetString(0)}/{reader.GetString(1)}/{reader.GetString(2)}/{reader.GetString(3)}/download"
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies, metadata
            FROM modules WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$ns", @namespace);
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
            PublishedAt = DateTime.Parse(reader.GetString(6)),
            Dependencies = deps,
            Metadata = DeserializeModuleMetadata(reader.GetString(8))
        };
    }

    public async Task<bool> UpdateModuleDescriptionAsync(string @namespace, string name, string provider,
        string description)
    {
        var sql = @"UPDATE modules SET description = $desc
            WHERE namespace = $ns AND name = $name AND provider = $prov AND deleted_at IS NULL";
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$ns", @namespace);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$prov", provider);
            cmd.Parameters.AddWithValue("$desc", description);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating description for module {Namespace}/{Name}/{Provider} in SQLite",
                @namespace, name, provider);
            return false;
        }
    }

    public async Task<ModuleExtractionDocument?> GetModuleExtractionAsync(string @namespace, string name, string provider,
        string version)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT e.document_json
            FROM module_extractions e
            JOIN modules m ON m.id = e.module_id
            WHERE m.namespace = $ns AND m.name = $name AND m.provider = $prov AND m.version = $ver";
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        var json = (string?)await cmd.ExecuteScalarAsync();
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ModuleExtractionDocument>(json);
    }

    public async Task UpsertModuleExtractionAsync(string @namespace, string name, string provider, string version,
        ModuleExtractionDocument document, string? sourceChecksum = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var now = DateTime.UtcNow.ToString("o");
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO module_extractions (module_id, document_json, source_checksum, created_at, updated_at)
            SELECT id, $document, $checksum, $now, $now
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL
            ON CONFLICT(module_id) DO UPDATE SET
                document_json = excluded.document_json,
                source_checksum = excluded.source_checksum,
                updated_at = excluded.updated_at";
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);
        cmd.Parameters.AddWithValue("$document", JsonSerializer.Serialize(document));
        cmd.Parameters.AddWithValue("$checksum", (object?)sourceChecksum ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateModuleMetadataAsync(string @namespace, string name, string provider, string version,
        Action<ModuleArtifactMetadata> mutate)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var readCmd = connection.CreateCommand();
        readCmd.CommandText = @"
            SELECT metadata
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";
        readCmd.Parameters.AddWithValue("$ns", @namespace);
        readCmd.Parameters.AddWithValue("$name", name);
        readCmd.Parameters.AddWithValue("$prov", provider);
        readCmd.Parameters.AddWithValue("$ver", version);

        var currentJson = (string?)await readCmd.ExecuteScalarAsync();
        var metadata = DeserializeModuleMetadata(currentJson);
        mutate(metadata);

        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE modules
            SET metadata = $metadata
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver AND deleted_at IS NULL";
        updateCmd.Parameters.AddWithValue("$ns", @namespace);
        updateCmd.Parameters.AddWithValue("$name", name);
        updateCmd.Parameters.AddWithValue("$prov", provider);
        updateCmd.Parameters.AddWithValue("$ver", version);
        updateCmd.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(metadata));

        await updateCmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesNeedingExtractionAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies, m.metadata
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL AND e.module_id IS NULL
            ORDER BY m.published_at
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }

    public async Task<ModuleExtractionAdminSummary> GetModuleExtractionAdminSummaryAsync()
    {
        var summary = new ModuleExtractionAdminSummary();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.metadata, CASE WHEN e.module_id IS NULL THEN 0 ELSE 1 END
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            summary.Total++;
            var metadata = DeserializeModuleMetadata(reader.IsDBNull(0) ? null : reader.GetString(0));
            IncrementStatus(summary, metadata.Extraction?.Status);

            if (Convert.ToInt32(reader.GetValue(1)) == 0)
                summary.NeverExtracted++;
        }

        return summary;
    }

    public async Task<ModuleExtractionAdminPage> ListModuleExtractionsAdminAsync(ModuleExtractionAdminQuery query)
    {
        var items = new List<ModuleExtractionAdminListItem>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata
            FROM modules m
            WHERE m.deleted_at IS NULL
            ORDER BY m.namespace, m.name, m.provider, m.version";

        await using var reader = await cmd.ExecuteReaderAsync();
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.metadata, e.document_json
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.namespace = $ns
              AND m.name = $name
              AND m.provider = $prov
              AND m.version = $ver
              AND m.deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        cmd.Parameters.AddWithValue("$ver", version);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var item = MapModuleExtractionAdminListItem(reader);
        var documentJson = reader.IsDBNull(6) ? null : reader.GetString(6);

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
            Documentation = item.Documentation,
            Document = string.IsNullOrWhiteSpace(documentJson)
                ? null
                : JsonSerializer.Deserialize<ModuleExtractionDocument>(documentJson)
        };
    }

    public async Task<IReadOnlyList<ModuleStorage>> ListModulesForExtractionBackfillAsync(int limit)
    {
        var modules = new List<ModuleStorage>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.storage_path, m.published_at, m.dependencies, m.metadata
            FROM modules m
            LEFT JOIN module_extractions e ON e.module_id = m.id
            WHERE m.deleted_at IS NULL
              AND (
                e.module_id IS NULL
                OR COALESCE(json_extract(m.metadata, '$.Extraction.Status'), 'pending') = 'failed'
              )
            ORDER BY m.published_at
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(MapModuleStorage(reader));
        }

        return modules;
    }

    private static async Task<List<string>> GetVersionsInternal(SqliteConnection connection, string @namespace,
        string name, string provider)
    {
        var versions = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            @"SELECT version FROM modules WHERE namespace = $ns AND name = $name AND provider = $prov AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("$ns", @namespace);
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

    private static ModuleExtractionAdminListItem MapModuleExtractionAdminListItem(SqliteDataReader reader)
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
            PublishedAt = DateTime.Parse(reader.GetString(6)),
            Dependencies = deps,
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

    // User & API Key methods
    public async Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, email, provider, provider_id, created_at, updated_at
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapUser(reader);
    }

    public async Task AddUserAsync(User user)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ($id, $email, $prov, $provId, $created, $updated)";

        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$prov", user.Provider);
        cmd.Parameters.AddWithValue("$provId", user.ProviderId);
        cmd.Parameters.AddWithValue("$created", user.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updated", user.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateUserAsync(User user)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE users SET 
                email = $email, provider = $prov, provider_id = $provId, 
                updated_at = $updated
            WHERE id = $id";

        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$prov", user.Provider);
        cmd.Parameters.AddWithValue("$provId", user.ProviderId);
        cmd.Parameters.AddWithValue("$updated", user.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteUserAsync(string userId)
    {
        const string sql = "DELETE FROM users WHERE id = $id";
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddApiKeyAsync(ApiKey apiKey)
    {
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM api_keys WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", apiKey.Id.ToString());

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordDownloadAsync(string @namespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Look up module_id
        await using var lookupCmd = connection.CreateCommand();
        lookupCmd.CommandText = "SELECT id FROM modules WHERE namespace = $ns AND name = $name AND provider = $provider AND version = $version AND deleted_at IS NULL";
        lookupCmd.Parameters.AddWithValue("$ns", @namespace);
        lookupCmd.Parameters.AddWithValue("$name", name);
        lookupCmd.Parameters.AddWithValue("$provider", provider);
        lookupCmd.Parameters.AddWithValue("$version", version);
        var moduleId = await lookupCmd.ExecuteScalarAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO module_downloads (module_id, namespace, name, provider, version, download_time, client_ip, user_agent)
                            VALUES ($moduleId, $ns, $name, $provider, $version, $time, $ip, $ua)";
        cmd.Parameters.AddWithValue("$moduleId", moduleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$provider", provider);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ip", (object?)clientIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ua", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<User>> ListAllUsersAsync()
    {
        var users = new List<User>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, email, provider, provider_id, created_at, updated_at FROM users ORDER BY created_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
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
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
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

    private static User MapUser(SqliteDataReader reader)
    {
        return new User
        {
            Id = reader.GetString(0),
            Email = reader.GetString(1),
            Provider = reader.GetString(2),
            ProviderId = reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            UpdatedAt = DateTime.Parse(reader.GetString(5))
        };
    }
}
