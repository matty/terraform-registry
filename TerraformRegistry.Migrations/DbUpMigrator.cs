using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;

namespace TerraformRegistry.Migrations;

/// <summary>
///     Runs database migrations using DbUp for both PostgreSQL and SQLite providers.
///     Handles bootstrapping of existing databases by pre-populating the journal.
/// </summary>
public class DbUpMigrator
{
    private readonly ILogger<DbUpMigrator> _logger;

    public DbUpMigrator(ILogger<DbUpMigrator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Runs all pending migrations for the specified database provider.
    /// </summary>
    /// <param name="provider">"postgres" or "sqlite"</param>
    /// <param name="connectionString">Database connection string</param>
    public void Migrate(string provider, string connectionString)
    {
        var scriptFilter = GetScriptFilter(provider);
        var upgrader = BuildUpgrader(provider, connectionString, scriptFilter);

        BootstrapExistingDatabase(provider, connectionString, upgrader);

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            _logger.LogError(result.Error, "Database migration failed at script: {Script}", result.ErrorScript?.Name);
            throw new InvalidOperationException($"Database migration failed: {result.Error.Message}", result.Error);
        }

        if (result.Scripts.Any())
        {
            _logger.LogInformation("Database migration completed. Executed {Count} script(s)", result.Scripts.Count());
            foreach (var script in result.Scripts)
            {
                _logger.LogInformation("  Executed: {Script}", script.Name);
            }
        }
        else
        {
            _logger.LogInformation("Database is already up to date");
        }
    }

    private UpgradeEngine BuildUpgrader(string provider, string connectionString, Func<string, bool> scriptFilter)
    {
        var dbUpLogger = new DbUpLogger(_logger);
        var assembly = Assembly.GetExecutingAssembly();

        return provider switch
        {
            "postgres" => DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(assembly, scriptFilter)
                .WithTransactionPerScript()
                .LogTo(dbUpLogger)
                .Build(),

            "sqlite" => DeployChanges.To
                .SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(assembly, scriptFilter)
                .WithTransactionPerScript()
                .LogTo(dbUpLogger)
                .Build(),

            _ => throw new ArgumentException($"Unsupported database provider: {provider}")
        };
    }

    private static Func<string, bool> GetScriptFilter(string provider)
    {
        var folder = provider switch
        {
            "postgres" => ".Scripts.PostgreSQL.",
            "sqlite" => ".Scripts.SQLite.",
            _ => throw new ArgumentException($"Unsupported database provider: {provider}")
        };

        return scriptName => scriptName.Contains(folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Detects existing databases and pre-populates the DbUp journal so baseline
    ///     scripts are not re-executed against already-migrated databases.
    ///     Uses DbUp's built-in MarkAsExecuted() API to write to the journal table
    ///     with the correct schema — no manual table creation needed.
    /// </summary>
    private void BootstrapExistingDatabase(string provider, string connectionString, UpgradeEngine upgrader)
    {
        var alreadyBootstrapped = upgrader.GetExecutedScripts().Count > 0;
        if (alreadyBootstrapped)
            return;

        var isExistingDatabase = provider switch
        {
            "postgres" => IsExistingPostgresDatabase(connectionString),
            "sqlite" => IsExistingSqliteDatabase(connectionString),
            _ => false
        };

        if (!isExistingDatabase)
            return;

        _logger.LogInformation("Detected existing {Provider} database — bootstrapping DbUp journal", provider);

        // Use DbUp's built-in MarkAsExecuted() to record all scripts in the journal
        // without actually running them. This creates the journal table with the
        // correct provider-specific schema automatically.
        var result = upgrader.MarkAsExecuted();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Failed to bootstrap DbUp journal for existing {provider} database: {result.Error.Message}",
                result.Error);
        }

        _logger.LogInformation("Bootstrapped {Count} script(s) in DbUp journal", result.Scripts.Count());
    }

    /// <summary>
    ///     Checks for legacy schema_version table and drops it in the same connection
    ///     to avoid an extra round-trip.
    /// </summary>
    private bool IsExistingPostgresDatabase(string connectionString)
    {
        using var conn = new Npgsql.NpgsqlConnection(connectionString);
        conn.Open();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = 'schema_version')";
        var exists = (bool)(checkCmd.ExecuteScalar() ?? false);

        if (exists)
        {
            using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS schema_version";
            dropCmd.ExecuteNonQuery();
            _logger.LogInformation("Removed legacy schema_version table");
        }

        return exists;
    }

    private static bool IsExistingSqliteDatabase(string connectionString)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='modules'";
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
