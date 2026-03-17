namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Creates the audit_logs table for tracking user actions.
/// </summary>
public class Migration_1_0_8 : IDatabaseMigration
{
    public string Version => "1.0.8";
    public string Description => "Create audit_logs table";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS audit_logs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                user_id TEXT,
                action VARCHAR(100) NOT NULL,
                resource_type VARCHAR(50) NOT NULL,
                resource_id TEXT,
                details TEXT,
                ip_address VARCHAR(50),
                timestamp TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_audit_logs_timestamp ON audit_logs(timestamp);
            CREATE INDEX IF NOT EXISTS idx_audit_logs_user ON audit_logs(user_id, timestamp);
            CREATE INDEX IF NOT EXISTS idx_audit_logs_action ON audit_logs(action, timestamp);
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
