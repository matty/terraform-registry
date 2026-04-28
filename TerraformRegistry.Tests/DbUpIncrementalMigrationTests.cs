using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.Migrations;

namespace TerraformRegistry.Tests;

public class DbUpIncrementalMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public DbUpIncrementalMigrationTests()
    {
        var dbName = $"IncrementalTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void Migration001_CreatesModulesTableWithIndexes()
    {
        MigrateUpTo(1, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("modules", tables);

        var columns = GetColumns(_connection, "modules");
        var expected = new[] { "id", "namespace", "name", "provider", "version", "description", "storage_path", "published_at", "dependencies", "deleted_at" };
        foreach (var col in expected)
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_modules_lookup", indexes);
        Assert.Contains("idx_modules_deleted_at", indexes);
    }

    [Fact]
    public void Migration002_CreatesUsersAndApiKeysTables()
    {
        MigrateUpTo(2, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("modules", tables);
        Assert.Contains("users", tables);
        Assert.Contains("api_keys", tables);

        var userColumns = GetColumns(_connection, "users");
        foreach (var col in new[] { "id", "email", "provider", "provider_id", "created_at", "updated_at" })
        {
            Assert.Contains(col, userColumns);
        }

        var apiKeyColumns = GetColumns(_connection, "api_keys");
        foreach (var col in new[] { "id", "user_id", "description", "token_hash", "prefix", "is_shared", "created_at", "expires_at", "last_used_at" })
        {
            Assert.Contains(col, apiKeyColumns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_api_keys_prefix", indexes);
    }

    [Fact]
    public void Migration003_SoftDeleteNoOp_PreviousTablesIntact()
    {
        MigrateUpTo(3, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("modules", tables);
        Assert.Contains("users", tables);
        Assert.Contains("api_keys", tables);

        // Verify the no-op script didn't break anything
        var columns = GetColumns(_connection, "modules");
        Assert.Contains("deleted_at", columns);
    }

    [Fact]
    public void Migration004_CreatesModuleDownloadsTable()
    {
        MigrateUpTo(4, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("module_downloads", tables);

        var columns = GetColumns(_connection, "module_downloads");
        foreach (var col in new[] { "id", "module_id", "namespace", "name", "provider", "version", "download_time", "client_ip", "user_agent" })
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_module_downloads_time", indexes);
    }

    [Fact]
    public void Migration005_CreatesWebhooksTable()
    {
        MigrateUpTo(5, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("webhooks", tables);

        var columns = GetColumns(_connection, "webhooks");
        foreach (var col in new[] { "id", "user_id", "url", "secret", "events", "is_active", "format", "template", "created_at", "updated_at" })
        {
            Assert.Contains(col, columns);
        }
    }

    [Fact]
    public void Migration006_CreatesVcsConnectionsTable()
    {
        MigrateUpTo(6, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("vcs_connections", tables);

        var columns = GetColumns(_connection, "vcs_connections");
        foreach (var col in new[] { "id", "label", "provider", "pat_encrypted", "default_org", "webhook_secret", "created_by", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, columns);
        }
    }

    [Fact]
    public void Migration007_CreatesVcsSourcesTable()
    {
        MigrateUpTo(7, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("vcs_sources", tables);

        var columns = GetColumns(_connection, "vcs_sources");
        foreach (var col in new[] { "id", "user_id", "namespace", "name", "provider", "repo_owner", "repo_name", "connection_id", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_vcs_sources_module", indexes);
        Assert.Contains("idx_vcs_sources_repo", indexes);
    }

    [Fact]
    public void Migration008_CreatesRbacTables()
    {
        MigrateUpTo(8, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("roles", tables);
        Assert.Contains("user_roles", tables);

        var roleColumns = GetColumns(_connection, "roles");
        foreach (var col in new[] { "id", "name", "description", "permissions", "is_system", "created_at", "updated_at" })
        {
            Assert.Contains(col, roleColumns);
        }

        var userRoleColumns = GetColumns(_connection, "user_roles");
        foreach (var col in new[] { "user_id", "role_id", "assigned_at", "assigned_by" })
        {
            Assert.Contains(col, userRoleColumns);
        }
    }

    [Fact]
    public void Migration009_CreatesAuditLogsTable()
    {
        MigrateUpTo(9, _connectionString);

        var tables = GetTables(_connection);
        Assert.Contains("audit_logs", tables);

        var columns = GetColumns(_connection, "audit_logs");
        foreach (var col in new[] { "id", "user_id", "action", "resource_type", "resource_id", "details", "ip_address", "timestamp" })
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_audit_logs_timestamp", indexes);
        Assert.Contains("idx_audit_logs_user", indexes);
        Assert.Contains("idx_audit_logs_action", indexes);
    }

    [Fact]
    public void Migration010_FixesConstraintsAndAddsIndexes()
    {
        MigrateUpTo(10, _connectionString);

        // Verify modules.description is now nullable (insert with NULL description)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO modules (namespace, name, provider, version, description, storage_path, published_at, dependencies)
            VALUES ('test', 'test', 'test', '1.0.0', NULL, '/path', '2026-01-01', '[]')";
        cmd.ExecuteNonQuery(); // Should not throw

        // Verify users has UNIQUE(provider, provider_id)
        cmd.CommandText = @"INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('u1', 'a@test.com', 'github', 'gh1', '2026-01-01', '2026-01-01')";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('u2', 'b@test.com', 'github', 'gh1', '2026-01-01', '2026-01-01')";
        var ex = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => cmd.ExecuteNonQuery());
        Assert.Contains("UNIQUE constraint failed", ex.Message);

        // Verify api_keys ON DELETE CASCADE works
        cmd.CommandText = @"INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at)
            VALUES ('k1', 'u1', 'key', 'hash', 'pfx', 0, '2026-01-01')";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DELETE FROM users WHERE id = 'u1'";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT COUNT(*) FROM api_keys WHERE user_id = 'u1'";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!); // Cascaded

        // Verify new indexes exist
        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_api_keys_user_id", indexes);
        Assert.Contains("idx_api_keys_is_shared", indexes);
        Assert.Contains("idx_webhooks_user_id", indexes);
        Assert.Contains("idx_vcs_sources_user", indexes);
        Assert.Contains("idx_vcs_sources_connection", indexes);
        Assert.Contains("idx_module_downloads_namespace", indexes);
        Assert.Contains("idx_module_downloads_name", indexes);
        Assert.Contains("idx_module_downloads_provider", indexes);
    }

    [Fact]
    public void Migration013_AddsModuleMetadataAndCreatesModuleExtractionsTable()
    {
        MigrateUpTo(13, _connectionString);

        var moduleColumns = GetColumns(_connection, "modules");
        Assert.Contains("metadata", moduleColumns);

        var tables = GetTables(_connection);
        Assert.Contains("module_extractions", tables);

        var extractionColumns = GetColumns(_connection, "module_extractions");
        foreach (var column in new[] { "module_id", "document_json", "source_checksum", "created_at", "updated_at" })
        {
            Assert.Contains(column, extractionColumns);
        }

        var indexes = GetIndexes(_connection);
        Assert.Contains("idx_module_extractions_updated_at", indexes);
    }

    [Fact]
    public void FullMigration_DataOperationsSucceed()
    {
        MigrateUpTo(10, _connectionString);

        using var cmd = _connection.CreateCommand();

        // Insert a module
        cmd.CommandText = @"
            INSERT INTO modules (namespace, name, provider, version, description, storage_path, published_at, dependencies)
            VALUES ('hashicorp', 'consul', 'aws', '1.0.0', 'Consul module', '/path/to/module', '2026-01-01T00:00:00Z', '[]')";
        cmd.ExecuteNonQuery();

        // Insert a user
        cmd.CommandText = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('user-1', 'test@example.com', 'github', 'gh-123', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert an api key
        cmd.CommandText = @"
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at)
            VALUES ('key-1', 'user-1', 'Test key', 'abc123hash', 'tfr_', 0, '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a module download
        cmd.CommandText = @"
            INSERT INTO module_downloads (module_id, namespace, name, provider, version, download_time)
            VALUES (1, 'hashicorp', 'consul', 'aws', '1.0.0', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a webhook
        cmd.CommandText = @"
            INSERT INTO webhooks (id, user_id, url, events, is_active, format, created_at, updated_at)
            VALUES ('wh-1', 'user-1', 'https://example.com/hook', 'module.published', 1, 'generic', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a vcs connection
        cmd.CommandText = @"
            INSERT INTO vcs_connections (id, label, provider, webhook_secret, is_active, created_at, updated_at)
            VALUES ('conn-1', 'GitHub Main', 'github', 'secret123', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a vcs source
        cmd.CommandText = @"
            INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active, created_at, updated_at)
            VALUES ('src-1', 'user-1', 'hashicorp', 'consul', 'aws', 'hashicorp', 'terraform-aws-consul', 'conn-1', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a role
        cmd.CommandText = @"
            INSERT INTO roles (id, name, description, permissions, is_system, created_at, updated_at)
            VALUES ('role-1', 'admin', 'Administrator', '[""*""]', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert a user role
        cmd.CommandText = @"
            INSERT INTO user_roles (user_id, role_id, assigned_at)
            VALUES ('user-1', 'role-1', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Insert an audit log
        cmd.CommandText = @"
            INSERT INTO audit_logs (id, user_id, action, resource_type, resource_id, details, ip_address, timestamp)
            VALUES ('log-1', 'user-1', 'module.publish', 'module', '1', '{""version"":""1.0.0""}', '127.0.0.1', '2026-01-01T00:00:00Z')";
        cmd.ExecuteNonQuery();

        // Verify all inserts by counting rows in each table
        var tableCounts = new Dictionary<string, long>();
        foreach (var table in new[] { "modules", "users", "api_keys", "module_downloads", "webhooks", "vcs_connections", "vcs_sources", "roles", "user_roles", "audit_logs" })
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            tableCounts[table] = (long)cmd.ExecuteScalar()!;
        }

        foreach (var (table, count) in tableCounts)
        {
            Assert.True(count >= 1, $"Expected at least 1 row in {table}, got {count}");
        }
    }

    private static void MigrateUpTo(int scriptNumber, string connectionString)
    {
        var maxPrefix = scriptNumber.ToString("D3");
        var assembly = typeof(DbUpMigrator).Assembly;

        var upgrader = DeployChanges.To
            .SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, s =>
            {
                if (!s.Contains(".Scripts.SQLite.", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Extract the NNN prefix from the script name
                // e.g. "TerraformRegistry.Migrations.Scripts.SQLite.001_initial_schema.sql"
                var fileName = s[(s.LastIndexOf('.', s.LastIndexOf('.') - 1) + 1)..];
                var prefix = fileName[..3];
                return string.Compare(prefix, maxPrefix, StringComparison.Ordinal) <= 0;
            })
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException($"Migration failed: {result.Error.Message}", result.Error);
        }
    }

    private static List<string> GetTables(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static List<string> GetColumns(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1)); // column 1 is "name"
        }
        return columns;
    }

    private static List<string> GetIndexes(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }
}
