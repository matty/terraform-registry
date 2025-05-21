using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TerraformRegistry.PostgreSQL.Migrations;

/// <summary>
///     Manages database migrations
/// </summary>
public class MigrationManager
{
    private const string SchemaVersionTable = "schema_version";
    private readonly ILogger<MigrationManager> _logger;
    private readonly List<IDatabaseMigration> _migrations = new();

    /// <summary>
    ///     Initializes a new instance of the MigrationManager class
    /// </summary>
    public MigrationManager(ILogger<MigrationManager> logger)
    {
        _logger = logger;
        // Discover all migration implementations in the assembly
        DiscoverMigrations();
    }

    /// <summary>
    ///     Uses reflection to find all classes that implement IDatabaseMigration
    /// </summary>
    private void DiscoverMigrations()
    {
        var migrationType = typeof(IDatabaseMigration);

        // Find all non-abstract classes that implement IDatabaseMigration in the current assembly
        var migrationTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && migrationType.IsAssignableFrom(t));

        foreach (var type in migrationTypes)
            // Create an instance of each migration class and add it to our list
            if (Activator.CreateInstance(type) is IDatabaseMigration migration)
                _migrations.Add(migration);

        // Sort migrations by version
        _migrations.Sort((a, b) => CompareVersions(a.Version, b.Version));
    }

    /// <summary>
    ///     Compares two version strings (SemVer format)
    /// </summary>
    private int CompareVersions(string versionA, string versionB)
    {
        var partsA = versionA.Split('.').Select(int.Parse).ToArray();
        var partsB = versionB.Split('.').Select(int.Parse).ToArray();

        // Compare major version
        var majorComparison = partsA[0].CompareTo(partsB[0]);
        if (majorComparison != 0) return majorComparison;

        // Compare minor version
        var minorComparison = partsA[1].CompareTo(partsB[1]);
        if (minorComparison != 0) return minorComparison;

        // Compare patch version
        return partsA[2].CompareTo(partsB[2]);
    }

    /// <summary>
    ///     Checks if the database needs initialization by looking for a version table
    /// </summary>
    public async Task<bool> NeedsInitializationAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the schema_version table exists
            var sql = $@"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = '{SchemaVersionTable}'
                );";

            await using var command = new NpgsqlCommand(sql, connection);
            var tableExists = (bool)await command.ExecuteScalarAsync(cancellationToken);

            if (!tableExists) return true; // Database needs initialization (table doesn't exist)

            // Check if there are any migrations to run
            return await HasPendingMigrationsAsync(connection, cancellationToken);
        }
        catch (PostgresException)
        {
            // If we got an exception, the database likely needs initialization
            return true;
        }
    }

    /// <summary>
    ///     Checks if there are any pending migrations that need to be applied
    /// </summary>
    private async Task<bool> HasPendingMigrationsAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        // Get the current database schema version
        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);

        // Check if there are any migrations with a higher version
        return _migrations.Any(m => CompareVersions(m.Version, currentVersion) > 0);
    }

    /// <summary>
    ///     Gets the current version of the database schema
    /// </summary>
    private async Task<string> GetCurrentVersionAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $"SELECT version FROM {SchemaVersionTable} ORDER BY applied_at DESC LIMIT 1;";
            await using var command = new NpgsqlCommand(sql, connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result?.ToString() ?? "0.0.0";
        }
        catch
        {
            // If there's any error, assume it's a new database
            return "0.0.0";
        }
    }

    /// <summary>
    ///     Initializes the database schema and applies all pending migrations
    /// </summary>
    public async Task InitializeDatabaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        // Create the schema_version table if it doesn't exist
        var createVersionTableSql = $@"
            CREATE TABLE IF NOT EXISTS {SchemaVersionTable} (
                id SERIAL PRIMARY KEY,
                version VARCHAR(50) NOT NULL,
                applied_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                description TEXT
            );";

        await using (var command = new NpgsqlCommand(createVersionTableSql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        // Get the current database version
        var currentVersion = await GetCurrentVersionAsync(connection);

        // Apply all migrations that have a higher version than the current version
        foreach (var migration in _migrations.Where(m => CompareVersions(m.Version, currentVersion) > 0))
            await ApplyMigrationAsync(connection, transaction, migration);
    }

    /// <summary>
    ///     Applies a single migration and records its version
    /// </summary>
    private async Task ApplyMigrationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IDatabaseMigration migration)
    {
        try
        {
            // Apply the migration
            await migration.ApplyAsync(connection, transaction);

            // Record that this migration was applied
            var insertVersionSql = $@"
                INSERT INTO {SchemaVersionTable} (version, description)
                VALUES (@version, @description);";

            await using var versionCommand = new NpgsqlCommand(insertVersionSql, connection, transaction);
            versionCommand.Parameters.AddWithValue("@version", migration.Version);
            versionCommand.Parameters.AddWithValue("@description", migration.Description);
            await versionCommand.ExecuteNonQueryAsync();

            _logger.LogInformation("Applied database migration to version {Version}: {Description}", migration.Version,
                migration.Description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying migration to version {Version}", migration.Version);
            throw;
        }
    }
}