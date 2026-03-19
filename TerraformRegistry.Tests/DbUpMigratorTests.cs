using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.Migrations;

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
    }

    [Fact]
    public void Migrate_FreshSqliteDatabase_CreatesAllTables()
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
        Assert.Contains("SchemaVersions", tables);
    }

    [Fact]
    public void Migrate_RunTwice_IsIdempotent()
    {
        var migrator = new DbUpMigrator(_logger);

        migrator.Migrate("sqlite", _connectionString);
        migrator.Migrate("sqlite", _connectionString); // Second run should be a no-op

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions";
        var count = (long)cmd.ExecuteScalar()!;

        // Should have exactly 9 SQLite scripts, not double
        Assert.Equal(9L, count);
    }

    [Fact]
    public void Migrate_ExistingSqliteDatabase_BootstrapsJournal()
    {
        // Simulate an existing database by creating the modules table manually
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
            )";
        setupCmd.ExecuteNonQuery();

        var migrator = new DbUpMigrator(_logger);
        migrator.Migrate("sqlite", _connectionString);

        // Journal should have entries (bootstrapped)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.True(count > 0);

        // Tables should still exist (not recreated)
        using var tableCmd = _connection.CreateCommand();
        tableCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='modules'";
        Assert.Equal(1L, (long)tableCmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migrate_UnsupportedProvider_ThrowsArgumentException()
    {
        var migrator = new DbUpMigrator(_logger);

        Assert.Throws<ArgumentException>(() => migrator.Migrate("mysql", "fake-connection"));
    }
}
