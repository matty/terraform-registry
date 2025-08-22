using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
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

    public SqliteDatabaseService(string connectionString, string baseUrl, ILogger<SqliteDatabaseService> logger)
    {
        _connectionString = connectionString;
        _baseUrl = baseUrl;
        _logger = logger;
    }

    public async Task InitializeDatabase()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var createSql = @"
        CREATE TABLE IF NOT EXISTS modules (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            namespace TEXT NOT NULL,
            name TEXT NOT NULL,
            provider TEXT NOT NULL,
            version TEXT NOT NULL,
            description TEXT NOT NULL,
            storage_path TEXT NOT NULL,
            published_at TEXT NOT NULL,
            dependencies TEXT NOT NULL,
            UNIQUE(namespace, name, provider, version)
        );";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = createSql;
        await cmd.ExecuteNonQueryAsync();

        // Helpful index for lookups
        var indexSql = "CREATE INDEX IF NOT EXISTS idx_modules_lookup ON modules(namespace, name, provider);";
        await using var idx = connection.CreateCommand();
        idx.CommandText = indexSql;
        await idx.ExecuteNonQueryAsync();
    }

    public async Task<ModuleList> ListModulesAsync(ModuleSearchRequest request)
    {
        var modules = new List<ModuleListItem>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Get latest version per (namespace,name,provider)
        var sql = @"
            WITH latest AS (
                SELECT namespace, name, provider, MAX(version) AS latest_version
                FROM modules
                GROUP BY namespace, name, provider
            )
            SELECT m.namespace, m.name, m.provider, m.version, m.description, m.published_at
            FROM modules m
            INNER JOIN latest l ON m.namespace = l.namespace AND m.name = l.name AND m.provider = l.provider AND m.version = l.latest_version
            WHERE 1=1";

        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            conditions.Add(" AND (lower(m.name) LIKE lower($q) OR lower(m.description) LIKE lower($q))");
            parameters.Add(new SqliteParameter("$q", $"%{request.Q}%"));
        }
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
        sql += " ORDER BY m.namespace, m.name, m.provider LIMIT $limit OFFSET $offset";
        parameters.Add(new SqliteParameter("$limit", request.Limit));
        parameters.Add(new SqliteParameter("$offset", request.Offset));

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
            var description = reader.GetString(4);
            var publishedAtIso = reader.GetString(5);

            // Fetch all versions for this module tuple
            var versions = await GetVersionsInternal(connection, ns, name, provider);

            modules.Add(new ModuleListItem
            {
                Id = $"{ns}/{name}/{provider}",
                Owner = ns,
                Namespace = ns,
                Name = name,
                Version = version,
                Provider = provider,
                Description = description,
                PublishedAt = publishedAtIso,
                Versions = versions,
                DownloadUrl = $"{_baseUrl}/v1/modules/{ns}/{name}/{provider}/{version}/download"
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

    public async Task<Module?> GetModuleAsync(string @namespace, string name, string provider, string version)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";

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
            Description = reader.GetString(4),
            Source = $"{_baseUrl}/{@namespace}/{name}",
            PublishedAt = publishedAtIso,
            DownloadUrl = $"{_baseUrl}/v1/modules/{@namespace}/{name}/{provider}/{version}/download",
            Versions = versions,
            Root = "main",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string> { { provider, "*" } }
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

    public async Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, version, description, storage_path, published_at, dependencies
            FROM modules
            WHERE namespace = $ns AND name = $name AND provider = $prov AND version = $ver";

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
            Dependencies = deps
        };
    }

    public async Task<bool> AddModuleAsync(ModuleStorage module)
    {
        var sql = @"
            INSERT INTO modules (
                namespace, name, provider, version, description, storage_path, published_at, dependencies
            ) VALUES (
                $ns, $name, $prov, $ver, $desc, $path, $published, $deps
            )
            ON CONFLICT(namespace, name, provider, version) DO UPDATE SET
                description = excluded.description,
                storage_path = excluded.storage_path,
                dependencies = excluded.dependencies";

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

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
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

    private static async Task<List<string>> GetVersionsInternal(SqliteConnection connection, string @namespace, string name, string provider)
    {
        var versions = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT version FROM modules WHERE namespace = $ns AND name = $name AND provider = $prov ORDER BY version DESC";
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prov", provider);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) versions.Add(r.GetString(0));
        return versions;
    }
}