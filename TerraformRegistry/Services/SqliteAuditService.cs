using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

public class SqliteAuditService : IAuditService
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteAuditService> _logger;

    public SqliteAuditService(string connectionString, ILogger<SqliteAuditService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task LogAsync(string? userId, string action, string resourceType, string? resourceId, object? details, string? ipAddress)
    {
        try
        {
            var detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO audit_logs (id, user_id, action, resource_type, resource_id, details, ip_address, timestamp)
                                VALUES ($id, $userId, $action, $resourceType, $resourceId, $details, $ipAddress, $timestamp)";

            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$userId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$resourceType", resourceType);
            cmd.Parameters.AddWithValue("$resourceId", (object?)resourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$details", (object?)detailsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ipAddress", (object?)ipAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to write audit log entry for action {Action}", action);
        }
    }

    public async Task<AuditLogPage> QueryAsync(string? action, string? userId, string? resourceType, DateTime? from, DateTime? toTimestamp, int limit = 50, int offset = 0)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(action))
        {
            conditions.Add("action = $action");
            parameters.Add(new SqliteParameter("$action", action));
        }

        if (!string.IsNullOrEmpty(userId))
        {
            conditions.Add("user_id = $userId");
            parameters.Add(new SqliteParameter("$userId", userId));
        }

        if (!string.IsNullOrEmpty(resourceType))
        {
            conditions.Add("resource_type = $resourceType");
            parameters.Add(new SqliteParameter("$resourceType", resourceType));
        }

        if (from.HasValue)
        {
            conditions.Add("timestamp >= $from");
            parameters.Add(new SqliteParameter("$from", from.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        if (toTimestamp.HasValue)
        {
            conditions.Add("timestamp <= $to");
            parameters.Add(new SqliteParameter("$to", toTimestamp.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        // Count query
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM audit_logs {whereClause}";
        foreach (var p in parameters) countCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

        // Data query
        await using var dataCmd = connection.CreateCommand();
        dataCmd.CommandText = $@"SELECT id, user_id, action, resource_type, resource_id, details, ip_address, timestamp
                                 FROM audit_logs {whereClause}
                                 ORDER BY timestamp DESC
                                 LIMIT $limit OFFSET $offset";

        foreach (var p in parameters) dataCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
        dataCmd.Parameters.AddWithValue("$limit", limit);
        dataCmd.Parameters.AddWithValue("$offset", offset);

        var entries = new List<AuditLogEntry>();
        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new AuditLogEntry(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
            ));
        }

        return new AuditLogPage(entries, total);
    }

    public async Task<AuditLogEntry?> GetAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, action, resource_type, resource_id, details, ip_address, timestamp FROM audit_logs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new AuditLogEntry(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
        );
    }
}
