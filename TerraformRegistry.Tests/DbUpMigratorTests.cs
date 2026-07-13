using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.Migrations;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests;

public class DbUpMigratorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;
    private readonly ILogger<DbUpMigrator> _logger;

    public DbUpMigratorTests()
    {
        // Use a unique shared in-memory database per test instance to avoid interference
        var dbName = $"DbUpTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open(); // Keep alive for duration of test
        _logger = Mock.Of<ILogger<DbUpMigrator>>();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MigrateFreshSqliteDatabaseCreatesAllTables()
    {
        var migrator = new DbUpMigrator(_logger);

        migrator.Migrate("sqlite", _connectionString);

        // Verify key tables exist
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));

        Assert.Contains("modules", tables);
        Assert.Contains("users", tables);
        Assert.Contains("api_keys", tables);
        Assert.Contains("module_downloads", tables);
        Assert.Contains("webhooks", tables);
        Assert.Contains("vcs_connections", tables);
        Assert.Contains("vcs_sources", tables);
        Assert.Contains("roles", tables);
        Assert.Contains("user_roles", tables);
        Assert.Contains("audit_logs", tables);
        Assert.Contains("providers", tables);
        Assert.Contains("provider_versions", tables);
        Assert.Contains("provider_platforms", tables);
        Assert.Contains("provider_gpg_keys", tables);
        Assert.Contains("provider_downloads", tables);
        Assert.Contains("mirror_provider_indexes", tables);
        Assert.Contains("mirror_provider_packages", tables);
        Assert.Contains("mirror_module_versions", tables);
        Assert.Contains("mirror_module_packages", tables);
        Assert.Contains("mirror_cache_leases", tables);
        Assert.Contains("module_publication_attempts", tables);
        Assert.Contains("module_extraction_jobs", tables);
        Assert.Contains("SchemaVersions", tables);
    }

    [Fact]
    public async Task MigrateFreshSqliteDatabaseAppliesEveryEmbeddedScriptAndSupportsCurrentVcsSyncSchema()
    {
        var migrator = new DbUpMigrator(_logger);

        migrator.Migrate("sqlite", _connectionString);

        Assert.Equal(
            GetEmbeddedScriptNames(".Scripts.SQLite."),
            GetSqliteJournalScriptNames(_connection));

        var columns = GetSqliteColumns(_connection, "vcs_sources");
        foreach (var column in new[] { "tag_pattern", "last_published_version", "last_sync_status", "last_sync_at", "last_sync_error" })
        {
            Assert.Contains(column, columns);
        }

        Assert.Contains("idx_vcs_sources_module_lookup", GetSqliteIndexes(_connection));

        await SeedSqliteUserAndConnectionAsync(_connection);

        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new SqliteVcsSourceService(_connectionString);
        var created = await service.CreateVcsSourceAsync(
            "user-1",
            "hashicorp",
            "consul",
            "aws",
            "hashicorp",
            "terraform-aws-consul",
            connectionId);

        var persisted = await service.GetByModuleAsync("hashicorp", "consul", "aws");

        Assert.NotNull(persisted);
        Assert.Equal(created.Id, persisted.Id);
        Assert.Equal("v*", persisted.TagPattern);
        Assert.Equal("never", persisted.LastSyncStatus);
        Assert.Null(persisted.LastPublishedVersion);
        Assert.Null(persisted.LastSyncAt);
        Assert.Null(persisted.LastSyncError);

        var updated = await service.UpdateSyncStateAsync(created.Id, "succeeded", "1.2.3", null);

        Assert.NotNull(updated);
        Assert.Equal("succeeded", updated.LastSyncStatus);
        Assert.Equal("1.2.3", updated.LastPublishedVersion);
        Assert.NotNull(updated.LastSyncAt);
        Assert.Null(updated.LastSyncError);
    }

    [Fact]
    public void MigrateRunTwiceIsIdempotent()
    {
        var migrator = new DbUpMigrator(_logger);

        migrator.Migrate("sqlite", _connectionString);
        migrator.Migrate("sqlite", _connectionString); // Second run should be a no-op

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions";
        var count = (long)cmd.ExecuteScalar()!;

        Assert.Equal(GetEmbeddedScriptNames(".Scripts.SQLite.").Count, count);
    }

    [Fact]
    public void MigrateExistingSqliteDatabaseWithLegacySchemaVersionBootstrapsOnlyAppliedScripts()
    {
        // Simulate a database that had 2 legacy migrations applied (like production on main)
        // by creating the tables those migrations would have created, plus the schema_version table
        using var setupCmd = _connection.CreateCommand();
        setupCmd.CommandText = @"
            CREATE TABLE modules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                namespace TEXT NOT NULL,
                name TEXT NOT NULL,
                provider TEXT NOT NULL,
                version TEXT NOT NULL,
                description TEXT NOT NULL,
                storage_path TEXT NOT NULL,
                published_at TEXT NOT NULL,
                dependencies TEXT NOT NULL,
                deleted_at TEXT NULL,
                UNIQUE(namespace, name, provider, version)
            );
            CREATE TABLE users (
                id TEXT PRIMARY KEY,
                email TEXT NOT NULL,
                provider TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(email)
            );
            CREATE TABLE api_keys (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                description TEXT NOT NULL,
                token_hash TEXT NOT NULL,
                prefix TEXT NOT NULL,
                is_shared INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                expires_at TEXT,
                last_used_at TEXT,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );
            CREATE TABLE schema_version (
                version TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            INSERT INTO schema_version VALUES ('1.0.0', 'Initial schema', '2026-01-01');
            INSERT INTO schema_version VALUES ('1.0.1', 'Users and API keys', '2026-01-01')";
        setupCmd.ExecuteNonQuery();

        var migrator = new DbUpMigrator(_logger);
        migrator.Migrate("sqlite", _connectionString);

        // Journal should contain every embedded SQLite script after bootstrapping legacy entries.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions";
        var journalCount = (long)cmd.ExecuteScalar()!;
        Assert.Equal(GetEmbeddedScriptNames(".Scripts.SQLite.").Count, journalCount);

        // Legacy schema_version table should be dropped
        using var svCmd = _connection.CreateCommand();
        svCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
        Assert.Equal(0L, (long)svCmd.ExecuteScalar()!);

        // New tables from scripts 003+ should exist
        using var tableCmd = _connection.CreateCommand();
        tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = tableCmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));

        Assert.Contains("roles", tables);
        Assert.Contains("user_roles", tables);
        Assert.Contains("audit_logs", tables);
        Assert.Contains("webhooks", tables);
        Assert.Contains("vcs_connections", tables);
        Assert.Contains("vcs_sources", tables);
        Assert.Contains("providers", tables);
        Assert.Contains("provider_versions", tables);
        Assert.Contains("provider_platforms", tables);
        Assert.Contains("provider_gpg_keys", tables);
        Assert.Contains("provider_downloads", tables);
        Assert.Contains("mirror_provider_indexes", tables);
        Assert.Contains("mirror_provider_packages", tables);
        Assert.Contains("mirror_module_versions", tables);
        Assert.Contains("mirror_module_packages", tables);
        Assert.Contains("mirror_cache_leases", tables);
    }

    [Fact]
    public void MigrateUnsupportedProviderThrowsArgumentException()
    {
        var migrator = new DbUpMigrator(_logger);

        Assert.Throws<ArgumentException>(() => migrator.Migrate("mysql", "fake-connection"));
    }

    [Fact]
    public void MigrateFailsClosedWhenApplicationSchemaIsUnjournaled()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "CREATE TABLE modules (id INTEGER PRIMARY KEY)";
        command.ExecuteNonQuery();

        var migrator = new DbUpMigrator(_logger);

        var exception = Assert.Throws<InvalidOperationException>(() => migrator.Migrate("sqlite", _connectionString));

        Assert.Contains("Unsafe database migration state", exception.Message);
        Assert.Contains("neither a legacy journal nor a DbUp journal entry", exception.Message);
        Assert.DoesNotContain("SchemaVersions", GetSqliteTableNames(_connection));
    }

    [Fact]
    public void MigrateFailsClosedWhenJournaledSchemaIsMissingRequiredTable()
    {
        var migrator = new DbUpMigrator(_logger);
        migrator.Migrate("sqlite", _connectionString);

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE audit_logs";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<InvalidOperationException>(() => migrator.Migrate("sqlite", _connectionString));

        Assert.Contains("Unsafe database migration state", exception.Message);
        Assert.Contains("009_audit_logs", exception.Message);
        Assert.Contains("audit_logs", exception.Message);
        Assert.Equal(GetEmbeddedScriptNames(".Scripts.SQLite.").Count, GetSqliteJournalEntryCount(_connection));
    }

    [Fact]
    public void MigrateFailsClosedWhenJournalHasGap()
    {
        var migrator = new DbUpMigrator(_logger);
        migrator.Migrate("sqlite", _connectionString);

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM SchemaVersions WHERE ScriptName LIKE '%002_users_and_api_keys%'";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var exception = Assert.Throws<InvalidOperationException>(() => migrator.Migrate("sqlite", _connectionString));

        Assert.Contains("Unsafe database migration state", exception.Message);
        Assert.Contains("not a contiguous prefix", exception.Message);
    }

    private static List<string> GetEmbeddedScriptNames(string providerFolder)
    {
        return typeof(DbUpMigrator).Assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(providerFolder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetSqliteJournalScriptNames(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ScriptName FROM SchemaVersions";
        using var reader = cmd.ExecuteReader();

        var scriptNames = new List<string>();
        while (reader.Read())
        {
            scriptNames.Add(reader.GetString(0));
        }

        return scriptNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long GetSqliteJournalEntryCount(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SchemaVersions";
        return (long)command.ExecuteScalar()!;
    }

    private static List<string> GetSqliteTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static List<string> GetSqliteColumns(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();

        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static List<string> GetSqliteIndexes(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'";
        using var reader = cmd.ExecuteReader();

        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task SeedSqliteUserAndConnectionAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('user-1', 'test@example.com', 'github', 'gh-123', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO vcs_connections (id, label, provider, webhook_secret, is_active, created_at, updated_at)
            VALUES ('11111111-1111-1111-1111-111111111111', 'GitHub Main', 'github', 'secret123', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();
    }
}
