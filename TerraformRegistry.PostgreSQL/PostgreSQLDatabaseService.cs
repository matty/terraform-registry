using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL.Migrations;

namespace TerraformRegistry.PostgreSQL;

/// <summary>
/// Implementation of a database service using PostgreSQL
/// </summary>
public class PostgreSqlDatabaseService : IDatabaseService, IInitializableDb
{
    private readonly string _connectionString;
    private readonly string _baseUrl;
    private readonly MigrationManager _migrationManager;
    private readonly ILogger<PostgreSqlDatabaseService> _logger;

    public PostgreSqlDatabaseService(string connectionString, string baseUrl, ILogger<PostgreSqlDatabaseService> logger, MigrationManager migrationManager)
    {
        _connectionString = connectionString;
        _baseUrl = baseUrl;
        _migrationManager = migrationManager;
        _logger = logger;
    }

    public async Task InitializeDatabase()
    {
        await InitializeDatabaseImpl();
    }

    private async Task InitializeDatabaseImpl()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        if (await _migrationManager.NeedsInitializationAsync(connection))
        {
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await _migrationManager.InitializeDatabaseAsync(connection, transaction);
                await transaction.CommitAsync();
                _logger.LogInformation("Database initialization and migrations completed successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error initializing database");
                throw;
            }
        }
    }

    /// <summary>
    /// Lists all modules based on search criteria
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
                        provider = m.provider
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
            WHERE 1=1";

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
    /// Gets detailed information about a specific module
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
        {
            // No matching module found
            return null;
        }

        // Dependencies are stored as a JSON array in PostgreSQL
        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();
        var versions = reader.GetFieldValue<string[]>(8);

        // Create and return the module
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
            Root = "main", // Set the root directory name as a string
            Submodules = new List<ModuleSubmodule>(), // No submodules for simplicity
            Providers = new Dictionary<string, string>
            {
                { provider, "*" } // Adding required Providers property with a default value
            }
        };
    }

    /// <summary>
    /// Gets all versions of a specific module
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
                provider = @provider
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
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetString(0));
        }

        // Return the updated ModuleVersions structure
        return new ModuleVersions
        {
            Versions = versions
        };
    }

    /// <summary>
    /// Gets the storage path information for a specific module version
    /// </summary>
    /// <remarks>
    /// This method retrieves the storage metadata that connects a module's metadata in the database 
    /// with its physical file location in blob storage. The FilePath property is particularly important
    /// as it's used by the blob storage service to locate and retrieve the actual module file.
    /// </remarks>
    public async Task<ModuleStorage?> GetModuleStorageAsync(string @namespace, string name, string provider, string version)
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
        {
            // No matching module found
            return null;
        }

        // Dependencies are stored as a JSON array in PostgreSQL
        var dependenciesJson = reader.GetString(7);
        var dependencies = JsonSerializer.Deserialize<List<string>>(dependenciesJson) ?? new List<string>();

        return new ModuleStorage
        {
            Namespace = reader.GetString(0),
            Name = reader.GetString(1),
            Provider = reader.GetString(2),
            Version = reader.GetString(3),
            Description = reader.GetString(4),
            FilePath = reader.GetString(5),  // Critical field that maps to blob storage
            PublishedAt = reader.GetDateTime(6),
            Dependencies = dependencies
        };
    }

    /// <summary>
    /// Adds a new module to the database
    /// </summary>
    /// <remarks>
    /// This method stores module metadata in the database, including a reference to the blob storage path
    /// where the actual module file is stored. The storage_path column creates the link between database
    /// records and physical files in the blob storage.
    /// </remarks>
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
            command.Parameters.AddWithValue("@storagePath", module.FilePath);  // Link to blob storage path
            command.Parameters.AddWithValue("@publishedAt", module.PublishedAt);
            command.Parameters.AddWithValue("@dependencies", module.Dependencies == null ? "[]" : JsonSerializer.Serialize(module.Dependencies)).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;

            // Execute and get the ID of the inserted/updated row
            var result = await command.ExecuteScalarAsync();

            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding module {Namespace}/{Name}/{Provider}/{Version} to database", module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }

    /// <summary>
    /// Removes a module from the database
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing module {Namespace}/{Name}/{Provider}/{Version} from database", module.Namespace, module.Name, module.Provider, module.Version);
            return false;
        }
    }
}