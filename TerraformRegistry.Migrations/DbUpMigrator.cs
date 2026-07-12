using System.Globalization;
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

        var executedScripts = upgrader.GetExecutedScripts();
        ValidateMigrationState(provider, connectionString, executedScripts);
        BootstrapExistingDatabase(provider, connectionString, upgrader, executedScripts);

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            MigrationLog.DatabaseMigrationFailed(_logger, result.Error, result.ErrorScript?.Name);
            throw new InvalidOperationException($"Database migration failed: {result.Error.Message}", result.Error);
        }

        var migratedScripts = result.Scripts.ToList();
        if (migratedScripts.Count > 0)
        {
            MigrationLog.DatabaseMigrationCompleted(_logger, migratedScripts.Count);
            foreach (var script in migratedScripts)
            {
                MigrationLog.ExecutedScript(_logger, script.Name);
            }
        }
        else
        {
            MigrationLog.DatabaseAlreadyUpToDate(_logger);
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
    ///     Refuses to guess how to recover a database whose journal and schema disagree.
    ///     DbUp's journal is the source of truth for a known upgrade path; silently editing it
    ///     can skip destructive scripts or make a partially applied migration permanent.
    /// </summary>
    private void ValidateMigrationState(
        string provider,
        string connectionString,
        List<string> executedScripts)
    {
        var allScripts = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(GetScriptFilter(provider))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (executedScripts.Any(script => !allScripts.Contains(script, StringComparer.OrdinalIgnoreCase)))
            ThrowUnsafeState(provider, "the DbUp journal contains a script that is not embedded in this application version");

        var legacyMigrationCount = provider switch
        {
            "postgres" => GetPostgresLegacyMigrationCount(connectionString, dropTable: false),
            "sqlite" => GetSqliteLegacyMigrationCount(connectionString, dropTable: false),
            _ => throw new ArgumentException($"Unsupported database provider: {provider}")
        };

        var hasLegacyJournal = provider switch
        {
            "postgres" => HasPostgresTable(connectionString, "schema_version"),
            "sqlite" => HasSqliteTable(connectionString, "schema_version"),
            _ => false
        };

        if (hasLegacyJournal && executedScripts.Count > 0)
            ThrowUnsafeState(provider, "both the legacy schema_version table and the DbUp journal are present");

        if (hasLegacyJournal && (legacyMigrationCount == 0 || legacyMigrationCount > allScripts.Count))
            ThrowUnsafeState(provider, $"the legacy schema_version table contains {legacyMigrationCount} entries, which cannot be mapped safely to {allScripts.Count} embedded scripts");

        if (executedScripts.Count == 0)
        {
            if (!hasLegacyJournal && HasApplicationSchema(provider, connectionString))
                ThrowUnsafeState(provider, "application tables exist but neither a legacy journal nor a DbUp journal entry identifies their migration state");

            if (hasLegacyJournal)
                ValidateSchemaForScripts(provider, connectionString, allScripts.Take(legacyMigrationCount));

            return;
        }

        var expectedPrefix = allScripts.Take(executedScripts.Count).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (executedScripts.Count != expectedPrefix.Count || !executedScripts.All(expectedPrefix.Contains))
            ThrowUnsafeState(provider, "the DbUp journal is not a contiguous prefix of the embedded migration chain");

        ValidateSchemaForScripts(provider, connectionString, executedScripts);
    }

    private static void ValidateSchemaForScripts(string provider, string connectionString, IEnumerable<string> scripts)
    {
        foreach (var scriptName in scripts)
        {
            foreach (var table in RequiredTablesForScript(scriptName))
            {
                var exists = provider switch
                {
                    "postgres" => HasPostgresTable(connectionString, table),
                    "sqlite" => HasSqliteTable(connectionString, table),
                    _ => false
                };

                if (!exists)
                    ThrowUnsafeState(provider, $"the journal records '{scriptName}' but required table '{table}' is missing");
            }
        }
    }

    private static IEnumerable<string> RequiredTablesForScript(string scriptName)
    {
        // These table sentinels cover every migration that creates durable state.  Migrations
        // which only add columns/indexes are intentionally not inferred from a table check.
        return scriptName switch
        {
            var name when name.Contains("001_initial_schema", StringComparison.OrdinalIgnoreCase) => ["modules"],
            var name when name.Contains("002_users_and_api_keys", StringComparison.OrdinalIgnoreCase) => ["users", "api_keys"],
            var name when name.Contains("004_downloads", StringComparison.OrdinalIgnoreCase) => ["module_downloads"],
            var name when name.Contains("005_webhooks", StringComparison.OrdinalIgnoreCase) => ["webhooks"],
            var name when name.Contains("006_vcs_connections", StringComparison.OrdinalIgnoreCase) => ["vcs_connections"],
            var name when name.Contains("007_vcs_sources", StringComparison.OrdinalIgnoreCase) => ["vcs_sources"],
            var name when name.Contains("008_rbac", StringComparison.OrdinalIgnoreCase) => ["roles", "user_roles"],
            var name when name.Contains("009_audit_logs", StringComparison.OrdinalIgnoreCase) => ["audit_logs"],
            var name when name.Contains("013_module_extractions", StringComparison.OrdinalIgnoreCase) => ["module_extractions"],
            var name when name.Contains("013_provider_registry", StringComparison.OrdinalIgnoreCase) => ["providers", "provider_versions", "provider_platforms"],
            var name when name.Contains("014_runtime_settings", StringComparison.OrdinalIgnoreCase) => ["runtime_settings"],
            var name when name.Contains("015_module_llm_contexts", StringComparison.OrdinalIgnoreCase) => ["module_llm_contexts"],
            var name when name.Contains("016_mirror_cache", StringComparison.OrdinalIgnoreCase) => ["mirror_provider_indexes", "mirror_provider_packages", "mirror_module_versions", "mirror_module_packages"],
            var name when name.Contains("terraform_authorization_codes", StringComparison.OrdinalIgnoreCase) => ["terraform_authorization_codes"],
            _ => []
        };
    }

    private static bool HasApplicationSchema(string provider, string connectionString) => provider switch
    {
        "postgres" => HasPostgresTable(connectionString, "modules") || HasPostgresTable(connectionString, "users"),
        "sqlite" => HasSqliteTable(connectionString, "modules") || HasSqliteTable(connectionString, "users"),
        _ => false
    };

    private static bool HasPostgresTable(string connectionString, string tableName)
    {
        using var conn = new Npgsql.NpgsqlConnection(connectionString);
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @name)";
        command.Parameters.AddWithValue("name", tableName);
        return (bool)(command.ExecuteScalar() ?? false);
    }

    private static bool HasSqliteTable(string connectionString, string tableName)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        return (long)(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static void ThrowUnsafeState(string provider, string detail) => throw new InvalidOperationException(
        $"Unsafe database migration state for {provider}: {detail}. The application will not modify this database; restore a verified backup or reconcile the journal and schema manually before retrying.");

    /// <summary>
    ///     Detects existing databases migrated by the legacy MigrationManager and
    ///     pre-populates the DbUp journal with only the scripts that correspond to
    ///     already-applied migrations. New scripts will then run normally.
    /// </summary>
    private void BootstrapExistingDatabase(string provider, string connectionString, UpgradeEngine upgrader, List<string> executedScripts)
    {
        if (executedScripts.Count > 0)
            return;

        var legacyMigrationCount = provider switch
        {
            "postgres" => GetPostgresLegacyMigrationCount(connectionString, dropTable: false),
            "sqlite" => GetSqliteLegacyMigrationCount(connectionString, dropTable: false),
            _ => 0
        };

        if (legacyMigrationCount == 0)
            return;

        MigrationLog.ExistingDatabaseDetected(_logger, provider, legacyMigrationCount);

        // Get the pending scripts sorted by name, then mark only those that
        // correspond to already-applied legacy migrations.
        var scriptsToMark = upgrader.GetScriptsToExecute()
            .OrderBy(s => s.Name, StringComparer.Ordinal)
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

        // Drop legacy table only after journal is safely populated
        _ = provider switch
        {
            "postgres" => GetPostgresLegacyMigrationCount(connectionString, dropTable: true),
            "sqlite" => GetSqliteLegacyMigrationCount(connectionString, dropTable: true),
            _ => 0
        };

        MigrationLog.BootstrappedScripts(_logger, scriptsToMark.Count);
        foreach (var script in scriptsToMark)
        {
            MigrationLog.MarkedAsExecuted(_logger, script.Name);
        }
    }

    /// <summary>
    ///     Repairs a journal that was over-bootstrapped by a previous buggy release.
    ///     If the journal says 008_rbac was executed but the roles table doesn't exist,
    ///     removes journal entries for scripts that weren't actually applied.
    ///     No-op once the database is healthy (short-circuits on roles table check).
    /// </summary>
    private void RepairOverBootstrappedJournal(string provider, string connectionString, UpgradeEngine upgrader, List<string> executedScripts)
    {
        if (provider != "postgres")
            return;

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

        MigrationLog.OverBootstrappedJournal(_logger);

        // Scripts that create new tables — check if the table actually exists.
        // Scripts 003 (ALTER ADD COLUMN) and 004 (ALTER FK) are idempotent and
        // have no sentinel table to check, so they are excluded.
        var scriptToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["005_webhooks"] = "webhooks",
            ["006_webhook_format"] = "webhooks",
            ["007_vcs_sources"] = "vcs_sources",
            ["008_rbac"] = "roles",
            ["009_audit_logs"] = "audit_logs",
            ["010_vcs_connections"] = "vcs_connections",
            ["011_schema_fixes"] = "webhooks",
        };

        var scriptsToRemove = new List<string>();

        foreach (var (scriptFragment, tableName) in scriptToTable)
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

        if (scriptsToRemove.Count == 0)
            return;

        foreach (var scriptName in scriptsToRemove)
        {
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM schemaversions WHERE scriptname = @name";
            deleteCmd.Parameters.AddWithValue("name", scriptName);
            deleteCmd.ExecuteNonQuery();
            MigrationLog.RemovedBogusJournalEntry(_logger, scriptName);
        }

        MigrationLog.RepairedJournal(_logger, scriptsToRemove.Count);
    }

    /// <summary>
    ///     Counts legacy migrations in the schema_version table, then drops it.
    ///     Returns 0 if the table does not exist (fresh database).
    /// </summary>
    private int GetPostgresLegacyMigrationCount(string connectionString, bool dropTable)
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
        var count = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (dropTable)
        {
            using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS schema_version";
            dropCmd.ExecuteNonQuery();
            MigrationLog.RemovedLegacySchemaVersion(_logger, count);
        }

        return count;
    }

    /// <summary>
    ///     Counts legacy migrations in the SQLite schema_version table, then drops it.
    ///     Returns 0 if the table does not exist (fresh database).
    /// </summary>
    private static int GetSqliteLegacyMigrationCount(string connectionString, bool dropTable)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        if ((long)(checkCmd.ExecuteScalar() ?? 0L) == 0)
            return 0;

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM schema_version";
        var count = Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (dropTable)
        {
            using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = "DROP TABLE schema_version";
            dropCmd.ExecuteNonQuery();
        }

        return count;
    }
}
