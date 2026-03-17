namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Creates the vcs_sources table for VCS integration.
/// </summary>
public class Migration_1_0_6 : IDatabaseMigration
{
    public string Version => "1.0.6";
    public string Description => "Create vcs_sources table";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS vcs_sources (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id TEXT NOT NULL,
                namespace VARCHAR(255) NOT NULL,
                name VARCHAR(255) NOT NULL,
                provider VARCHAR(255) NOT NULL,
                repo_owner VARCHAR(255) NOT NULL,
                repo_name VARCHAR(255) NOT NULL,
                pat_encrypted TEXT,
                webhook_secret TEXT NOT NULL,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_vcs_sources_module ON vcs_sources(namespace, name, provider);
            CREATE INDEX IF NOT EXISTS idx_vcs_sources_repo ON vcs_sources(repo_owner, repo_name);
            CREATE INDEX IF NOT EXISTS idx_vcs_sources_user ON vcs_sources(user_id);
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
