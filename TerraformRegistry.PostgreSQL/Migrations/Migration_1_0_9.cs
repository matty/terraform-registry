namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Creates vcs_connections table and migrates vcs_sources to use connection_id.
/// </summary>
public class Migration_1_0_9 : IDatabaseMigration
{
    public string Version => "1.0.9";
    public string Description => "Create vcs_connections table and refactor vcs_sources schema";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS vcs_connections (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                label TEXT NOT NULL,
                provider TEXT NOT NULL DEFAULT 'github',
                pat_encrypted TEXT,
                default_org TEXT,
                webhook_secret TEXT NOT NULL,
                created_by TEXT,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_vcs_connections_active ON vcs_connections(is_active);

            DROP TABLE IF EXISTS vcs_sources;
            CREATE TABLE vcs_sources (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id TEXT NOT NULL,
                namespace VARCHAR(255) NOT NULL,
                name VARCHAR(255) NOT NULL,
                provider VARCHAR(255) NOT NULL,
                repo_owner VARCHAR(255) NOT NULL,
                repo_name VARCHAR(255) NOT NULL,
                connection_id UUID NOT NULL REFERENCES vcs_connections(id),
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_vcs_sources_module ON vcs_sources(namespace, name, provider);
            CREATE INDEX IF NOT EXISTS idx_vcs_sources_repo ON vcs_sources(repo_owner, repo_name);
            CREATE INDEX IF NOT EXISTS idx_vcs_sources_user ON vcs_sources(user_id);
            CREATE INDEX IF NOT EXISTS idx_vcs_sources_connection ON vcs_sources(connection_id);
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
