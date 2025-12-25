using Npgsql;
using TerraformRegistry.PostgreSQL.Migrations;

namespace TerraformRegistry.PostgreSQL.Migrations;

public class Migration_1_0_2 : IDatabaseMigration
{
    public string Version => "1.0.2";
    public string Description => "Add Provider Registry tables";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        // GPG Keys table
        var createGpgKeysSql = @"
            CREATE TABLE IF NOT EXISTS gpg_keys (
                key_id VARCHAR(50) PRIMARY KEY,
                namespace VARCHAR(255) NOT NULL,
                ascii_armor TEXT NOT NULL,
                trust_signature TEXT NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_gpg_keys_namespace ON gpg_keys(namespace);
        ";

        await using (var command = new NpgsqlCommand(createGpgKeysSql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        // Provider Versions table (Optional, if we want to query versions directly via SQL instead of file system scan)
        // Since the requirements involve listing versions and finding packages, a DB approach is robust.
        // Assuming we store provider metadata in DB similar to modules.

        var createProvidersSql = @"
            CREATE TABLE IF NOT EXISTS providers (
                id SERIAL PRIMARY KEY,
                namespace VARCHAR(255) NOT NULL,
                type VARCHAR(255) NOT NULL,
                version VARCHAR(50) NOT NULL,
                os VARCHAR(50) NOT NULL,
                arch VARCHAR(50) NOT NULL,
                filename VARCHAR(255) NOT NULL,
                download_url VARCHAR(1024) NOT NULL,
                shasum VARCHAR(255) NOT NULL,
                protocols TEXT NOT NULL, -- JSON array of supported protocols
                signing_key_id VARCHAR(50) NOT NULL,
                published_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT uq_provider_version_platform UNIQUE (namespace, type, version, os, arch),
                CONSTRAINT fk_providers_signing_key FOREIGN KEY (signing_key_id) REFERENCES gpg_keys(key_id)
            );
            CREATE INDEX IF NOT EXISTS idx_providers_lookup ON providers(namespace, type);
        ";

        await using (var command = new NpgsqlCommand(createProvidersSql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }
    }
}
