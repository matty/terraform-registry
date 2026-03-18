using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlWebhookService : IWebhookService
{
    private readonly string _connectionString;

    public PostgreSqlWebhookService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Webhook>> ListWebhooksAsync(string userId)
    {
        var webhooks = new List<Webhook>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE user_id = @userId ORDER BY created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            webhooks.Add(MapWebhook(reader));
        }

        return webhooks;
    }

    public async Task<IEnumerable<Webhook>> ListAllWebhooksAsync()
    {
        var webhooks = new List<Webhook>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks ORDER BY created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            webhooks.Add(MapWebhook(reader));
        }

        return webhooks;
    }

    public async Task<Webhook> CreateWebhookAsync(string userId, string url, string[] events, string? secret, string format = "generic", string? template = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Url = url,
            Events = events,
            Secret = secret,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Format = format,
            Template = template
        };

        var sql = @"INSERT INTO webhooks (id, user_id, url, secret, events, is_active, created_at, updated_at, format, template)
                    VALUES (@id, @userId, @url, @secret, @events, @isActive, @createdAt, @updatedAt, @format, @template)";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", webhook.Id);
        cmd.Parameters.AddWithValue("@userId", webhook.UserId);
        cmd.Parameters.AddWithValue("@url", webhook.Url);
        cmd.Parameters.AddWithValue("@secret", (object?)webhook.Secret ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("@events", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = webhook.Events });
        cmd.Parameters.AddWithValue("@isActive", webhook.IsActive);
        cmd.Parameters.AddWithValue("@createdAt", webhook.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", webhook.UpdatedAt);
        cmd.Parameters.AddWithValue("@format", format);
        cmd.Parameters.AddWithValue("@template", (object?)template ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return webhook;
    }

    public async Task<Webhook?> UpdateWebhookAsync(Guid webhookId, string userId, string? url, string[]? events, string? secret, bool? isActive, string? format, string? template)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Build dynamic UPDATE (RETURNING handles not-found/access-denied in one round-trip)
        var setClauses = new List<string> { "updated_at = @updatedAt" };
        var parameters = new List<NpgsqlParameter>
        {
            new("@id", webhookId),
            new("@userId", userId),
            new("@updatedAt", DateTime.UtcNow)
        };

        if (url != null)
        {
            setClauses.Add("url = @url");
            parameters.Add(new NpgsqlParameter("@url", url));
        }

        if (events != null)
        {
            setClauses.Add("events = @events");
            parameters.Add(new NpgsqlParameter("@events", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = events });
        }

        if (secret != null)
        {
            setClauses.Add("secret = @secret");
            parameters.Add(new NpgsqlParameter("@secret", secret));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = @isActive");
            parameters.Add(new NpgsqlParameter("@isActive", isActive.Value));
        }

        if (format != null)
        {
            setClauses.Add("format = @format");
            parameters.Add(new NpgsqlParameter("@format", format));
        }

        if (template != null)
        {
            setClauses.Add("template = @template");
            parameters.Add(new NpgsqlParameter("@template", template));
        }

        var sql = $"UPDATE webhooks SET {string.Join(", ", setClauses)} WHERE id = @id AND user_id = @userId RETURNING id, user_id, url, secret, events, is_active, created_at, updated_at, format, template";
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapWebhook(reader);
    }

    public async Task<Webhook?> GetWebhookAsync(Guid webhookId, string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE id = @id AND user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", webhookId);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapWebhook(reader);
    }

    public async Task<bool> DeleteWebhookAsync(Guid webhookId, string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM webhooks WHERE id = @id AND user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", webhookId);
        cmd.Parameters.AddWithValue("@userId", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IEnumerable<Webhook>> GetActiveWebhooksForEventAsync(string eventType)
    {
        var webhooks = new List<Webhook>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE is_active = true AND @eventType = ANY(events)";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@eventType", eventType);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            webhooks.Add(MapWebhook(reader));
        }

        return webhooks;
    }

    private static Webhook MapWebhook(NpgsqlDataReader reader)
    {
        return new Webhook
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetString(1),
            Url = reader.GetString(2),
            Secret = reader.IsDBNull(3) ? null : reader.GetString(3),
            Events = reader.GetFieldValue<string[]>(4),
            IsActive = reader.GetBoolean(5),
            CreatedAt = reader.GetDateTime(6),
            UpdatedAt = reader.GetDateTime(7),
            Format = reader.GetString(8),
            Template = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }
}
