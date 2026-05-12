using Microsoft.Extensions.Logging;
using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.PostgreSQL.Repositories;

public sealed class PostgreSqlModuleDownloadRecorder(
    string connectionString,
    ILogger logger) : IModuleDownloadRecorder
{
    public async Task RecordDownloadAsync(string moduleNamespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT record_module_download(@p0, @p1, @p2, @p3, @p4, @p5)", conn);
            cmd.Parameters.AddWithValue("@p0", moduleNamespace);
            cmd.Parameters.AddWithValue("@p1", name);
            cmd.Parameters.AddWithValue("@p2", provider);
            cmd.Parameters.AddWithValue("@p3", version);
            cmd.Parameters.AddWithValue("@p4", (object?)clientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@p5", (object?)userAgent ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex)
        {
            RegistryLog.Warning(logger, ex, "Failed to record download for {Namespace}/{Name}/{Provider}/{Version}",
                moduleNamespace, name, provider, version);
        }
    }
}
