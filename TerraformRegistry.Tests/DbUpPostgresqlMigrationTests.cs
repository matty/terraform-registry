using System.Reflection;
using DbUp;
using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using TerraformRegistry.Migrations;

namespace TerraformRegistry.Tests;

[Trait("Category", "Integration")]
public class DbUpPostgresqlMigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithDatabase("migration_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _postgresContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task Migration001_CreatesModulesAndDownloadsTablesWithFunction()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(1, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("modules", tables);
        Assert.Contains("module_downloads", tables);

        var moduleColumns = GetColumns(conn, "modules");
        foreach (var col in new[] { "id", "namespace", "name", "provider", "version", "description", "storage_path", "published_at", "dependencies", "metadata" })
        {
            Assert.Contains(col, moduleColumns);
        }

        var downloadColumns = GetColumns(conn, "module_downloads");
        foreach (var col in new[] { "id", "module_id", "namespace", "name", "provider", "version", "download_time", "client_ip", "user_agent" })
        {
            Assert.Contains(col, downloadColumns);
        }

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_module_namespace", indexes);
        Assert.Contains("idx_module_name", indexes);
        Assert.Contains("idx_module_provider", indexes);
        Assert.Contains("idx_module_version", indexes);
        Assert.Contains("idx_module_metadata", indexes);
        Assert.Contains("idx_downloads_module_id", indexes);
        Assert.Contains("idx_downloads_time", indexes);

        // Verify the PL/pgSQL function exists
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_proc WHERE proname = 'record_module_download')";
        var functionExists = (bool)(await cmd.ExecuteScalarAsync())!;
        Assert.True(functionExists, "record_module_download function should exist");
    }

    [Fact]
    public async Task Migration002_CreatesUsersAndApiKeysTables()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(2, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("modules", tables);
        Assert.Contains("users", tables);
        Assert.Contains("api_keys", tables);

        var userColumns = GetColumns(conn, "users");
        foreach (var col in new[] { "id", "email", "provider", "provider_id", "created_at", "updated_at" })
        {
            Assert.Contains(col, userColumns);
        }

        var apiKeyColumns = GetColumns(conn, "api_keys");
        foreach (var col in new[] { "id", "user_id", "description", "token_hash", "prefix", "is_shared", "created_at", "expires_at", "last_used_at" })
        {
            Assert.Contains(col, apiKeyColumns);
        }

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_api_keys_prefix", indexes);
        Assert.Contains("idx_api_keys_user_id", indexes);
    }

    [Fact]
    public async Task Migration003_AddsDeletedAtColumnToModules()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(3, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var columns = GetColumns(conn, "modules");
        Assert.Contains("deleted_at", columns);

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_modules_deleted_at", indexes);
    }

    [Fact]
    public async Task Migration004_UpdatesForeignKeyToCascade()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(4, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT confdeltype FROM pg_constraint WHERE conname = 'module_downloads_module_id_fkey'";
        var deleteType = (char)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal('c', deleteType); // 'c' means CASCADE
    }

    [Fact]
    public async Task Migration005_CreatesWebhooksTable()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(5, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("webhooks", tables);

        var columns = GetColumns(conn, "webhooks");
        foreach (var col in new[] { "id", "user_id", "url", "secret", "events", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, columns);
        }
    }

    [Fact]
    public async Task Migration006_AddsFormatAndTemplateToWebhooks()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(6, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var columns = GetColumns(conn, "webhooks");
        Assert.Contains("format", columns);
        Assert.Contains("template", columns);
    }

    [Fact]
    public async Task Migration007_CreatesVcsSourcesTable()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(7, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("vcs_sources", tables);

        var columns = GetColumns(conn, "vcs_sources");
        foreach (var col in new[] { "id", "user_id", "namespace", "name", "provider", "repo_owner", "repo_name", "pat_encrypted", "webhook_secret", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_vcs_sources_module", indexes);
        Assert.Contains("idx_vcs_sources_repo", indexes);
    }

    [Fact]
    public async Task Migration008_CreatesRbacTables()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(8, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("roles", tables);
        Assert.Contains("user_roles", tables);

        var roleColumns = GetColumns(conn, "roles");
        foreach (var col in new[] { "id", "name", "description", "permissions", "is_system", "created_at", "updated_at" })
        {
            Assert.Contains(col, roleColumns);
        }

        var userRoleColumns = GetColumns(conn, "user_roles");
        foreach (var col in new[] { "user_id", "role_id", "assigned_at", "assigned_by" })
        {
            Assert.Contains(col, userRoleColumns);
        }
    }

    [Fact]
    public async Task Migration009_CreatesAuditLogsTable()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(9, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("audit_logs", tables);

        var columns = GetColumns(conn, "audit_logs");
        foreach (var col in new[] { "id", "user_id", "action", "resource_type", "resource_id", "details", "ip_address", "timestamp" })
        {
            Assert.Contains(col, columns);
        }

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_audit_logs_timestamp", indexes);
        Assert.Contains("idx_audit_logs_user", indexes);
        Assert.Contains("idx_audit_logs_action", indexes);
    }

    [Fact]
    public async Task Migration010_CreatesVcsConnectionsAndRebuildsVcsSources()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(10, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("vcs_connections", tables);
        Assert.Contains("vcs_sources", tables);

        var connectionColumns = GetColumns(conn, "vcs_connections");
        foreach (var col in new[] { "id", "label", "provider", "pat_encrypted", "default_org", "webhook_secret", "created_by", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, connectionColumns);
        }

        // vcs_sources should now have connection_id instead of pat_encrypted/webhook_secret
        var sourceColumns = GetColumns(conn, "vcs_sources");
        foreach (var col in new[] { "id", "user_id", "namespace", "name", "provider", "repo_owner", "repo_name", "connection_id", "is_active", "created_at", "updated_at" })
        {
            Assert.Contains(col, sourceColumns);
        }
        Assert.DoesNotContain("pat_encrypted", sourceColumns);
        Assert.DoesNotContain("webhook_secret", sourceColumns);

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_vcs_connections_active", indexes);
        Assert.Contains("idx_vcs_sources_connection", indexes);
    }

    [Fact]
    public async Task Migration011_AddsWebhooksUserIdIndex()
    {
        var connStr = CreateFreshDatabase();
        MigrateUpTo(11, connStr);

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'idx_webhooks_user_id'";
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        Assert.Equal("idx_webhooks_user_id", result!.ToString());
    }

    [Fact]
    public async Task Migration014_CreatesModuleExtractionsTable()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(14, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("module_extractions", tables);

        var columns = GetColumns(conn, "module_extractions");
        foreach (var column in new[] { "module_id", "document_json", "source_checksum", "created_at", "updated_at" })
        {
            Assert.Contains(column, columns);
        }

        var indexes = GetIndexes(conn);
        Assert.Contains("idx_module_extractions_updated_at", indexes);
    }

    [Fact]
    public async Task Migration015_CreatesRuntimeSettingsTable()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(15, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = GetTables(conn);
        Assert.Contains("runtime_settings", tables);

        var columns = GetColumns(conn, "runtime_settings");
        Assert.Contains("key", columns);
        Assert.Contains("value_json", columns);
        Assert.Contains("updated_at", columns);
        Assert.Contains("updated_by", columns);
    }

    [Fact]
    public async Task FullMigration_DataOperationsSucceed()
    {
        var connectionString = CreateFreshDatabase();

        MigrateUpTo(11, connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();

        // Insert a module
        cmd.CommandText = @"
            INSERT INTO modules (namespace, name, provider, version, description, storage_path, published_at, dependencies)
            VALUES ('hashicorp', 'consul', 'aws', '1.0.0', 'Consul module', '/path/to/module', '2026-01-01T00:00:00Z', '[]')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a user
        cmd.CommandText = @"
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES ('user-1', 'test@example.com', 'github', 'gh-123', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert an api key (UUID type)
        cmd.CommandText = @"
            INSERT INTO api_keys (id, user_id, description, token_hash, prefix, is_shared, created_at)
            VALUES ('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'user-1', 'Test key', 'abc123hash', 'tfr_', false, '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a module download
        cmd.CommandText = @"
            INSERT INTO module_downloads (module_id, namespace, name, provider, version, download_time)
            VALUES (1, 'hashicorp', 'consul', 'aws', '1.0.0', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Test the record_module_download function
        cmd.CommandText = @"SELECT record_module_download('hashicorp', 'consul', 'aws', '1.0.0', '10.0.0.1', 'test-agent')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a webhook (UUID type, TEXT[] for events, BOOLEAN)
        cmd.CommandText = @"
            INSERT INTO webhooks (id, user_id, url, events, is_active, format, created_at, updated_at)
            VALUES (gen_random_uuid(), 'user-1', 'https://example.com/hook', ARRAY['module.published'], true, 'generic', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a vcs connection
        cmd.CommandText = @"
            INSERT INTO vcs_connections (id, label, provider, webhook_secret, is_active, created_at, updated_at)
            VALUES ('b0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'GitHub Main', 'github', 'secret123', true, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a vcs source (with connection_id FK)
        cmd.CommandText = @"
            INSERT INTO vcs_sources (id, user_id, namespace, name, provider, repo_owner, repo_name, connection_id, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), 'user-1', 'hashicorp', 'consul', 'aws', 'hashicorp', 'terraform-aws-consul', 'b0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', true, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a role (UUID, TEXT[] for permissions, BOOLEAN)
        cmd.CommandText = @"
            INSERT INTO roles (id, name, description, permissions, is_system, created_at, updated_at)
            VALUES ('c0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'admin', 'Administrator', ARRAY['*'], true, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert a user role
        cmd.CommandText = @"
            INSERT INTO user_roles (user_id, role_id, assigned_at)
            VALUES ('user-1', 'c0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Insert an audit log
        cmd.CommandText = @"
            INSERT INTO audit_logs (id, user_id, action, resource_type, resource_id, details, ip_address, timestamp)
            VALUES (gen_random_uuid(), 'user-1', 'module.publish', 'module', '1', '{""version"":""1.0.0""}', '127.0.0.1', '2026-01-01T00:00:00Z')";
        await cmd.ExecuteNonQueryAsync();

        // Verify all inserts by counting rows in each table
        foreach (var table in new[] { "modules", "users", "api_keys", "module_downloads", "webhooks", "vcs_connections", "vcs_sources", "roles", "user_roles", "audit_logs" })
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.True(count >= 1, $"Expected at least 1 row in {table}, got {count}");
        }

        // Verify the function-based download was recorded (should be 2 total downloads)
        cmd.CommandText = "SELECT COUNT(*) FROM module_downloads";
        var downloadCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(2, downloadCount);
    }

    [Fact]
    public async Task Migrate_ExistingPostgresDatabase_WithLegacySchemaVersion_BootstrapsOnlyAppliedScripts()
    {
        var connectionString = CreateFreshDatabase();

        // Simulate a production database from main: only scripts 001+002 were applied
        // via the old MigrationManager. Create those tables + the legacy schema_version table.
        MigrateUpTo(2, connectionString);

        // Remove the DbUp journal (simulates pre-DbUp state) and add legacy schema_version
        await using var setupConn = new NpgsqlConnection(connectionString);
        await setupConn.OpenAsync();
        await using var dropJournal = setupConn.CreateCommand();
        dropJournal.CommandText = "DROP TABLE IF EXISTS schemaversions";
        await dropJournal.ExecuteNonQueryAsync();

        await using var createSv = setupConn.CreateCommand();
        createSv.CommandText = @"
            CREATE TABLE schema_version (
                version TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO schema_version VALUES ('1.0.0', 'Initial schema', NOW());
            INSERT INTO schema_version VALUES ('1.0.1', 'Users and API keys', NOW())";
        await createSv.ExecuteNonQueryAsync();

        // Now run the full DbUp migrator — should bootstrap 2, execute remaining 10
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbUpMigrator>();
        var migrator = new DbUpMigrator(logger);
        migrator.Migrate("postgres", connectionString);

        // Verify all tables exist (including new ones from scripts 003-011)
        await using var verifyConn = new NpgsqlConnection(connectionString);
        await verifyConn.OpenAsync();
        var tables = GetTables(verifyConn);

        Assert.Contains("modules", tables);
        Assert.Contains("users", tables);
        Assert.Contains("api_keys", tables);
        Assert.Contains("webhooks", tables);            // 005
        Assert.Contains("vcs_sources", tables);         // 007
        Assert.Contains("roles", tables);               // 008
        Assert.Contains("user_roles", tables);          // 008
        Assert.Contains("audit_logs", tables);          // 009
        Assert.Contains("vcs_connections", tables);     // 010

        // Verify legacy schema_version was dropped
        await using var svCheck = verifyConn.CreateCommand();
        svCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = 'schema_version')";
        Assert.False((bool)(await svCheck.ExecuteScalarAsync())!);

        // Verify journal has all scripts recorded
        await using var journalCmd = verifyConn.CreateCommand();
        journalCmd.CommandText = "SELECT COUNT(*) FROM schemaversions";
        var journalCount = (long)(await journalCmd.ExecuteScalarAsync())!;
        Assert.Equal(14, journalCount);
    }

    [Fact]
    public async Task Migrate_ExistingPostgresDatabase_MarkAsExecuted_DoesNotOverMark()
    {
        var connectionString = CreateFreshDatabase();

        // Simulate: only 1 old migration applied
        MigrateUpTo(1, connectionString);

        await using var setupConn = new NpgsqlConnection(connectionString);
        await setupConn.OpenAsync();
        await using var dropJournal = setupConn.CreateCommand();
        dropJournal.CommandText = "DROP TABLE IF EXISTS schemaversions";
        await dropJournal.ExecuteNonQueryAsync();

        await using var createSv = setupConn.CreateCommand();
        createSv.CommandText = @"
            CREATE TABLE schema_version (
                version TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO schema_version VALUES ('1.0.0', 'Initial schema', NOW())";
        await createSv.ExecuteNonQueryAsync();

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbUpMigrator>();
        var migrator = new DbUpMigrator(logger);
        migrator.Migrate("postgres", connectionString);

        // All tables should exist — 1 bootstrapped, remaining scripts executed
        await using var verifyConn = new NpgsqlConnection(connectionString);
        await verifyConn.OpenAsync();
        var tables = GetTables(verifyConn);

        Assert.Contains("users", tables);          // 002 — executed, not bootstrapped
        Assert.Contains("roles", tables);           // 008
        Assert.Contains("user_roles", tables);      // 008

        await using var journalCmd = verifyConn.CreateCommand();
        journalCmd.CommandText = "SELECT COUNT(*) FROM schemaversions";
        var journalCount = (long)(await journalCmd.ExecuteScalarAsync())!;
        Assert.Equal(14, journalCount);
    }

    private string CreateFreshDatabase()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        using var conn = new NpgsqlConnection(_postgresContainer.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        cmd.ExecuteNonQuery();

        var builder = new NpgsqlConnectionStringBuilder(_postgresContainer.GetConnectionString())
        {
            Database = dbName
        };
        return builder.ConnectionString;
    }

    private static void MigrateUpTo(int scriptNumber, string connectionString)
    {
        var maxPrefix = scriptNumber.ToString("D3");
        var assembly = typeof(DbUpMigrator).Assembly;

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, s =>
            {
                if (!s.Contains(".Scripts.PostgreSQL.", StringComparison.OrdinalIgnoreCase))
                    return false;

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

    private static List<string> GetTables(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static List<string> GetColumns(NpgsqlConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table";
        cmd.Parameters.AddWithValue("table", tableName);
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private static List<string> GetIndexes(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'";
        using var reader = cmd.ExecuteReader();
        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }
}
