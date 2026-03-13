namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Adds ON DELETE CASCADE to module_downloads FK so purging modules
/// with download history succeeds instead of silently failing.
/// </summary>
public class Migration_1_0_3 : IDatabaseMigration
{
    public string Version => "1.0.3";
    public string Description => "Add ON DELETE CASCADE to module_downloads foreign key";

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var sql = @"
            ALTER TABLE module_downloads
                DROP CONSTRAINT IF EXISTS module_downloads_module_id_fkey;
            ALTER TABLE module_downloads
                ADD CONSTRAINT module_downloads_module_id_fkey
                FOREIGN KEY (module_id) REFERENCES modules(id) ON DELETE CASCADE;
        ";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
