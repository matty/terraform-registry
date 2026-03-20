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
        RepairOverBootstrappedJournal(provider, connectionString, upgrader);

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
    ///     Detects existing databases migrated by the legacy MigrationManager and
    ///     pre-populates the DbUp journal with only the scripts that correspond to
    ///     already-applied migrations. New scripts will then run normally.
    /// </summary>
    private void BootstrapExistingDatabase(string provider, string connectionString, UpgradeEngine upgrader)
    {
        var alreadyBootstrapped = upgrader.GetExecutedScripts().Count > 0;
        if (alreadyBootstrapped)
            return;

        var legacyMigrationCount = provider switch
        {
            "postgres" => GetPostgresLegacyMigrationCount(connectionString),
            "sqlite" => GetSqliteLegacyMigrationCount(connectionString),
            _ => 0
        };

        if (legacyMigrationCount == 0)
            return;

        _logger.LogInformation(
            "Detected existing {Provider} database with {Count} legacy migration(s) — bootstrapping DbUp journal",
            provider, legacyMigrationCount);

        // Get the pending scripts sorted by name, then mark only those that
        // correspond to already-applied legacy migrations.
        var scriptsToMark = upgrader.GetScriptsToExecute()
            .OrderBy(s => s.Name)
            .Take(legacyMigrationCount)
            .ToList();

        // Build a separate upgrader that only contains the scripts to mark,
        // then use MarkAsExecuted() to record them in the journal.
        var scriptFilter = GetScriptFilter(provider);
        var scriptNames = scriptsToMark.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bootstrapUpgrader = BuildUpgrader(provider, connectionString,
            name => scriptFilter(name) && scriptNames.Contains(name));

        var result = bootstrapUpgrader.MarkAsExecuted();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Failed to bootstrap DbUp journal for existing {provider} database: {result.Error.Message}",
                result.Error);
        }

        _logger.LogInformation("Bootstrapped {Count} script(s) in DbUp journal", scriptsToMark.Count);
        foreach (var script in scriptsToMark)
        {
            _logger.LogInformation("  Marked as executed: {Script}", script.Name);
        }
    }

    /// <summary>
    ///     Repairs a journal that was over-bootstrapped by a previous buggy release.
    ///     If the journal says 008_rbac was executed but the roles table doesn't exist,
    ///     removes journal entries for scripts that weren't actually applied.
    ///     No-op once the database is healthy (short-circuits on roles table check).
    /// </summary>
    private void RepairOverBootstrappedJournal(string provider, string connectionString, UpgradeEngine upgrader)
    {
        if (provider != "postgres")
            return;

        var executedScripts = upgrader.GetExecutedScripts();
        if (executedScripts.Count == 0)
            return;

        var hasRbacInJournal = executedScripts.Any(s => s.Contains("008_rbac", StringComparison.OrdinalIgnoreCase));
        if (!hasRbacInJournal)
            return;

        using var conn = new Npgsql.NpgsqlConnection(connectionString);
        conn.Open();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = 'roles')";
        var rolesExist = (bool)(checkCmd.ExecuteScalar() ?? false);

        if (rolesExist)
            return;

        _logger.LogWarning("Detected over-bootstrapped DbUp journal — roles table missing despite journal entry. Repairing...");

        // Scripts that create new tables — check if the table actually exists
        var tableChecks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["003_soft_delete"] = "deleted_modules",
            ["005_webhooks"] = "webhooks",
            ["007_vcs_sources"] = "vcs_sources",
            ["008_rbac"] = "roles",
            ["009_audit_logs"] = "audit_logs",
            ["010_vcs_connections"] = "vcs_connections",
        };

        // Scripts that ALTER existing tables — check if their prerequisite table exists
        var alterChecks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["004_fk_cascading"] = "deleted_modules",
            ["006_webhook_format"] = "webhooks",
            ["011_schema_fixes"] = "webhooks",
        };

        var scriptsToRemove = new List<string>();

        foreach (var checks in new[] { tableChecks, alterChecks })
        {
            foreach (var (scriptFragment, tableName) in checks)
            {
                var journalEntry = executedScripts.FirstOrDefault(s =>
                    s.Contains(scriptFragment, StringComparison.OrdinalIgnoreCase));
                if (journalEntry == null)
                    continue;

                using var tableCheck = conn.CreateCommand();
                tableCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = @name)";
                tableCheck.Parameters.AddWithValue("name", tableName);

                if (!(bool)(tableCheck.ExecuteScalar() ?? false))
                    scriptsToRemove.Add(journalEntry);
            }
        }

        if (scriptsToRemove.Count == 0)
            return;

        foreach (var scriptName in scriptsToRemove)
        {
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM schemaversions WHERE scriptname = @name";
            deleteCmd.Parameters.AddWithValue("name", scriptName);
            deleteCmd.ExecuteNonQuery();
            _logger.LogInformation("  Removed bogus journal entry: {Script}", scriptName);
        }

        _logger.LogInformation("Repaired journal — removed {Count} over-bootstrapped entries. Scripts will now run.", scriptsToRemove.Count);
    }

    /// <summary>
    ///     Counts legacy migrations in the schema_version table, then drops it.
    ///     Returns 0 if the table does not exist (fresh database).
    /// </summary>
    private int GetPostgresLegacyMigrationCount(string connectionString)
    {
        using var conn = new Npgsql.NpgsqlConnection(connectionString);
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = 'schema_version')";
        var exists = (bool)(checkCmd.ExecuteScalar() ?? false);
        if (!exists)
            return 0;

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM schema_version";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());

        using var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = "DROP TABLE IF EXISTS schema_version";
        dropCmd.ExecuteNonQuery();
        _logger.LogInformation("Removed legacy schema_version table ({Count} entries)", count);

        return count;
    }

    /// <summary>
    ///     Counts legacy migrations in the SQLite schema_version table, then drops it.
    ///     Returns 0 if the table does not exist (fresh database).
    /// </summary>
    private static int GetSqliteLegacyMigrationCount(string connectionString)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        if ((long)(checkCmd.ExecuteScalar() ?? 0L) == 0)
            return 0;

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM schema_version";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());

        using var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = "DROP TABLE schema_version";
        dropCmd.ExecuteNonQuery();

        return count;
    }
}
