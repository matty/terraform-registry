namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Adds format and template columns to the webhooks table.
/// </summary>
public class Migration_1_0_5 : IDatabaseMigration
{
    public string Version => "1.0.5";
    public string Description => "Add format and template columns to webhooks";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            ALTER TABLE webhooks ADD COLUMN IF NOT EXISTS format TEXT NOT NULL DEFAULT 'generic';
            ALTER TABLE webhooks ADD COLUMN IF NOT EXISTS template TEXT;
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
