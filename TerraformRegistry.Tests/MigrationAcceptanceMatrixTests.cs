using System.Globalization;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.Migrations;

namespace TerraformRegistry.Tests;

/// <summary>
/// The SQLite portion of the Phase 0 acceptance matrix. PostgreSQL exercises the
/// equivalent fixtures in <see cref="DbUpPostgresqlMigrationTests"/>, which runs in
/// the phase gate because it requires a Testcontainer.
/// </summary>
public sealed class MigrationAcceptanceMatrixTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public MigrationAcceptanceMatrixTests()
    {
        var databaseName = $"MigrationMatrix_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FreshChainAppliesEveryScriptAndSecondRunIsANoOp()
    {
        var migrator = CreateMigrator();

        migrator.Migrate("sqlite", _connectionString);
        var firstJournal = GetJournal();

        migrator.Migrate("sqlite", _connectionString);

        Assert.Equal(GetEmbeddedScriptNames(), firstJournal);
        Assert.Equal(firstJournal, GetJournal());
        Assert.Contains("modules", GetTables());
        Assert.Contains("vcs_sources", GetTables());
    }

    [Fact]
    public void PopulatedPre010ChainPreservesRowsRelationshipsAndForeignKeys()
    {
        MigrateUpTo(9);
        SeedPre010Fixture();

        var migrator = CreateMigrator();
        migrator.Migrate("sqlite", _connectionString);

        AssertRowCount("modules", "id = 42", 1);
        AssertRowCount("module_downloads", "id = 84 AND module_id = 42", 1);
        AssertRowCount("users", "id = 'user-1'", 1);
        AssertRowCount("api_keys", "id = 'key-1' AND user_id = 'user-1'", 1);
        AssertRowCount("webhooks", "id = 'hook-1' AND user_id = 'user-1'", 1);
        AssertRowCount("vcs_sources", "id = 'source-1' AND user_id = 'user-1'", 1);
        AssertRowCount("user_roles", "user_id = 'user-1' AND role_id = 'role-1'", 1);

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT connection_id
            FROM vcs_sources
            WHERE id = 'source-1'";
        Assert.Equal("connection-1", (string)command.ExecuteScalar()!);

        command.CommandText = "PRAGMA foreign_key_check";
        using var foreignKeyCheck = command.ExecuteReader();
        Assert.False(foreignKeyCheck.Read());

        // The full migrator must also be safe to run after a populated upgrade.
        migrator.Migrate("sqlite", _connectionString);
        AssertRowCount("vcs_sources", "id = 'source-1' AND connection_id = 'connection-1'", 1);
    }

    [Fact]
    public void AlreadyJournaledChainRetainsRowsAndDoesNotReapplyScripts()
    {
        var migrator = CreateMigrator();
        migrator.Migrate("sqlite", _connectionString);

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO modules (id, namespace, name, provider, version, description, storage_path, published_at, dependencies)
            VALUES (42, 'hashicorp', 'consul', 'aws', '1.0.0', 'Consul', '/modules/consul', '2026-01-01T00:00:00Z', '[]');";
        command.ExecuteNonQuery();
        var journalBefore = GetJournal();

        migrator.Migrate("sqlite", _connectionString);

        Assert.Equal(journalBefore, GetJournal());
        AssertRowCount("modules", "id = 42 AND description = 'Consul'", 1);
    }

    [Fact]
    public void InterruptedJournalChainFailsClosedWithoutChangingRowsOrJournal()
    {
        var migrator = CreateMigrator();
        migrator.Migrate("sqlite", _connectionString);

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = @"
                INSERT INTO modules (id, namespace, name, provider, version, description, storage_path, published_at, dependencies)
                VALUES (42, 'hashicorp', 'consul', 'aws', '1.0.0', 'Consul', '/modules/consul', '2026-01-01T00:00:00Z', '[]');
                DELETE FROM SchemaVersions WHERE ScriptName LIKE '%002_users_and_api_keys%';";
            command.ExecuteNonQuery();
        }

        var journalBefore = GetJournal();
        var exception = Assert.Throws<InvalidOperationException>(() => migrator.Migrate("sqlite", _connectionString));

        Assert.Contains("Unsafe database migration state", exception.Message);
        Assert.Contains("not a contiguous prefix", exception.Message);
        Assert.Equal(journalBefore, GetJournal());
        AssertRowCount("modules", "id = 42 AND description = 'Consul'", 1);
    }

    private static DbUpMigrator CreateMigrator() => new(NullLogger<DbUpMigrator>.Instance);

    private void MigrateUpTo(int scriptNumber)
    {
        var maximumPrefix = scriptNumber.ToString("D3", CultureInfo.InvariantCulture);
        var upgrader = DeployChanges.To
            .SqliteDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrator).Assembly, scriptName =>
            {
                if (!scriptName.Contains(".Scripts.SQLite.", StringComparison.OrdinalIgnoreCase))
                    return false;

                var fileName = scriptName[(scriptName.LastIndexOf('.', scriptName.LastIndexOf('.') - 1) + 1)..];
                return string.Compare(fileName[..3], maximumPrefix, StringComparison.Ordinal) <= 0;
            })
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        Assert.True(result.Successful, result.Error?.ToString());
    }

    private void SeedPre010Fixture()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO modules (id, namespace, name, provider, version, description, storage_path, published_at, dependencies)
            VALUES (42, 'hashicorp', 'consul', 'aws', '1.0.0', 'Consul', '/modules/consul', '2026-01-01T00:00:00Z', '[]');
            INSERT INTO module_downloads (id, module_id, namespace, name, provider, version, download_time, client_ip, user_agent)
            VALUES (84, 42, 'hashicorp', 'consul', 'aws', '1.0.0', '2026-01-02T00:00:00Z', '127.0.0.1', 'migration-matrix');
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('user-1', 'user@example.com', 'github', '123', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at)
            VALUES ('key-1', 'user-1', 'key', 'hash', 'tfr_', 0, '2026-01-01T00:00:00Z');
            INSERT INTO webhooks (id, user_id, url, events, is_active, format, created_at, updated_at)
            VALUES ('hook-1', 'user-1', 'https://example.com/hook', 'module.published', 1, 'generic', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO vcs_connections (id, label, provider, webhook_secret, is_active, created_at, updated_at)
            VALUES ('connection-1', 'GitHub', 'github', 'secret', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active, created_at, updated_at)
            VALUES ('source-1', 'user-1', 'hashicorp', 'consul', 'aws', 'hashicorp', 'terraform-aws-consul', 'connection-1', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO roles (id, name, description, permissions, is_system, created_at, updated_at)
            VALUES ('role-1', 'reader', 'Reader', '[]', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO user_roles (user_id, role_id, assigned_at)
            VALUES ('user-1', 'role-1', '2026-01-01T00:00:00Z');";
        command.ExecuteNonQuery();
    }

    private List<string> GetJournal()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName";
        using var reader = command.ExecuteReader();
        var scripts = new List<string>();
        while (reader.Read())
            scripts.Add(reader.GetString(0));
        return scripts;
    }

    private List<string> GetTables()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static List<string> GetEmbeddedScriptNames() => typeof(DbUpMigrator).Assembly
        .GetManifestResourceNames()
        .Where(name => name.Contains(".Scripts.SQLite.", StringComparison.OrdinalIgnoreCase))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    private void AssertRowCount(string table, string predicate, long expected)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {predicate}";
        Assert.Equal(expected, (long)command.ExecuteScalar()!);
    }
}
