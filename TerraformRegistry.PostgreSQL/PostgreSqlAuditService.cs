using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlAuditService : IAuditService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlAuditService> _logger;

    public PostgreSqlAuditService(string connectionString, ILogger<PostgreSqlAuditService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task LogAsync(string? userId, string action, string resourceType, string? resourceId, object? details, string? ipAddress)
    {
        try
        {
            var detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"INSERT INTO audit_logs (id, user_id, action, resource_type, resource_id, details, ip_address)
                        VALUES (@id, @userId, @action, @resourceType, @resourceId, @details, @ipAddress)";

            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@action", action);
            cmd.Parameters.AddWithValue("@resourceType", resourceType);
            cmd.Parameters.AddWithValue("@resourceId", (object?)resourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@details", (object?)detailsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ipAddress", (object?)ipAddress ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to write audit log entry for action {Action}", action);
        }
    }

    public async Task<AuditLogPage> QueryAsync(string? action, string? userId, string? resourceType, DateTime? from, DateTime? toTimestamp, int limit = 50, int offset = 0)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(action))
        {
            conditions.Add("action = @action");
            parameters.Add(new NpgsqlParameter("@action", action));
        }

        if (!string.IsNullOrEmpty(userId))
        {
            conditions.Add("user_id = @userId");
            parameters.Add(new NpgsqlParameter("@userId", userId));
        }

        if (!string.IsNullOrEmpty(resourceType))
        {
            conditions.Add("resource_type = @resourceType");
            parameters.Add(new NpgsqlParameter("@resourceType", resourceType));
        }

        if (from.HasValue)
        {
            conditions.Add("timestamp >= @from");
            parameters.Add(new NpgsqlParameter("@from", from.Value));
        }

        if (toTimestamp.HasValue)
        {
            conditions.Add("timestamp <= @to");
            parameters.Add(new NpgsqlParameter("@to", toTimestamp.Value));
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        // Count query
        var countSql = $"SELECT COUNT(*) FROM audit_logs {whereClause}";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        foreach (var p in parameters) countCmd.Parameters.Add(p.Clone());
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

        // Data query
        var dataSql = $@"SELECT id, user_id, action, resource_type, resource_id, details, ip_address, timestamp
                         FROM audit_logs {whereClause}
                         ORDER BY timestamp DESC
                         LIMIT @limit OFFSET @offset";

        await using var dataCmd = new NpgsqlCommand(dataSql, connection);
        foreach (var p in parameters) dataCmd.Parameters.Add(p.Clone());
        dataCmd.Parameters.AddWithValue("@limit", limit);
        dataCmd.Parameters.AddWithValue("@offset", offset);

        var entries = new List<AuditLogEntry>();
        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new AuditLogEntry(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDateTime(7)
            ));
        }

        return new AuditLogPage(entries, total);
    }

    public async Task<AuditLogEntry?> GetAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, user_id, action, resource_type, resource_id, details, ip_address, timestamp FROM audit_logs WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new AuditLogEntry(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDateTime(7)
        );
    }
}
