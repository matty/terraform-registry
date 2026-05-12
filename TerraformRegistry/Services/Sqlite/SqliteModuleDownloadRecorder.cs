using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services.Sqlite;

public sealed class SqliteModuleDownloadRecorder(string connectionString) : IModuleDownloadRecorder
{
    public async Task RecordDownloadAsync(string moduleNamespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Look up module_id
        await using var lookupCmd = connection.CreateCommand();
        lookupCmd.CommandText = "SELECT id FROM modules WHERE namespace = $ns AND name = $name AND provider = $provider AND version = $version AND deleted_at IS NULL";
        lookupCmd.Parameters.AddWithValue("$ns", moduleNamespace);
        lookupCmd.Parameters.AddWithValue("$name", name);
        lookupCmd.Parameters.AddWithValue("$provider", provider);
        lookupCmd.Parameters.AddWithValue("$version", version);
        var moduleId = await lookupCmd.ExecuteScalarAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO module_downloads (module_id, namespace, name, provider, version, download_time, client_ip, user_agent)
                            VALUES ($moduleId, $ns, $name, $provider, $version, $time, $ip, $ua)";
        cmd.Parameters.AddWithValue("$moduleId", moduleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ns", moduleNamespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$provider", provider);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ip", (object?)clientIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ua", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
