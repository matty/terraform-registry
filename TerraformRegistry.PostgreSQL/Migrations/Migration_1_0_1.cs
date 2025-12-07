namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Adds User and ApiKey tables
/// </summary>
public class Migration_1_0_1 : IDatabaseMigration
{
    public string Version => "1.0.1";
    public string Description => "Add users and api_keys tables";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS users (
                id VARCHAR(255) PRIMARY KEY,
                email VARCHAR(255) NOT NULL,
                provider VARCHAR(50) NOT NULL,
                provider_id VARCHAR(255) NOT NULL,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT uq_users_email UNIQUE (email),
                CONSTRAINT uq_users_provider_id UNIQUE (provider, provider_id)
            );

            CREATE TABLE IF NOT EXISTS api_keys (
                id UUID PRIMARY KEY,
                user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                description VARCHAR(255) NOT NULL,
                token_hash TEXT NOT NULL,
                prefix VARCHAR(10) NOT NULL,
                is_shared BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                expires_at TIMESTAMP WITH TIME ZONE,
                last_used_at TIMESTAMP WITH TIME ZONE
            );

            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
            CREATE INDEX IF NOT EXISTS idx_api_keys_user_id ON api_keys(user_id);
            CREATE INDEX IF NOT EXISTS idx_api_keys_prefix ON api_keys(prefix);
            CREATE INDEX IF NOT EXISTS idx_api_keys_is_shared ON api_keys(is_shared); -- Optimization for shared key lookups
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
