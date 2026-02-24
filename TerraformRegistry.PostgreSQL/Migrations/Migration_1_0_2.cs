namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Adds deleted_at column to modules table for soft delete support
/// </summary>
public class Migration_1_0_2 : IDatabaseMigration
{
    public string Version => "1.0.2";
    public string Description => "Add deleted_at column to modules for soft delete";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            ALTER TABLE modules ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP WITH TIME ZONE NULL;
            CREATE INDEX IF NOT EXISTS idx_modules_deleted_at ON modules(deleted_at);
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
