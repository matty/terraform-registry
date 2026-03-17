using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class SqliteWebhookService : IWebhookService
{
    private readonly string _connectionString;

    public SqliteWebhookService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Webhook>> ListWebhooksAsync(string userId)
    {
        var webhooks = new List<Webhook>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE user_id = $userId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("$userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            webhooks.Add(MapWebhook(reader));
        }

        return webhooks;
    }

    public async Task<Webhook> CreateWebhookAsync(string userId, string url, string[] events, string? secret, string format = "generic", string? template = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
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

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO webhooks (id, user_id, url, secret, events, is_active, created_at, updated_at, format, template)
                            VALUES ($id, $userId, $url, $secret, $events, $isActive, $createdAt, $updatedAt, $format, $template)";
        cmd.Parameters.AddWithValue("$id", webhook.Id.ToString());
        cmd.Parameters.AddWithValue("$userId", webhook.UserId);
        cmd.Parameters.AddWithValue("$url", webhook.Url);
        cmd.Parameters.AddWithValue("$secret", (object?)webhook.Secret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$events", JsonSerializer.Serialize(webhook.Events));
        cmd.Parameters.AddWithValue("$isActive", webhook.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", webhook.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", webhook.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$format", format);
        cmd.Parameters.AddWithValue("$template", (object?)template ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return webhook;
    }

    public async Task<Webhook?> UpdateWebhookAsync(Guid webhookId, string userId, string? url, string[]? events, string? secret, bool? isActive, string? format, string? template)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check ownership
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT id FROM webhooks WHERE id = $id AND user_id = $userId";
        checkCmd.Parameters.AddWithValue("$id", webhookId.ToString());
        checkCmd.Parameters.AddWithValue("$userId", userId);
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists == null) return null;

        // Build dynamic UPDATE
        var setClauses = new List<string> { "updated_at = $updatedAt" };
        var parameters = new List<SqliteParameter>
        {
            new("$id", webhookId.ToString()),
            new("$userId", userId),
            new("$updatedAt", DateTime.UtcNow.ToString("o"))
        };

        if (url != null)
        {
            setClauses.Add("url = $url");
            parameters.Add(new SqliteParameter("$url", url));
        }

        if (events != null)
        {
            setClauses.Add("events = $events");
            parameters.Add(new SqliteParameter("$events", JsonSerializer.Serialize(events)));
        }

        if (secret != null)
        {
            setClauses.Add("secret = $secret");
            parameters.Add(new SqliteParameter("$secret", secret));
        }

        if (isActive.HasValue)
        {
            setClauses.Add("is_active = $isActive");
            parameters.Add(new SqliteParameter("$isActive", isActive.Value ? 1 : 0));
        }

        if (format != null)
        {
            setClauses.Add("format = $format");
            parameters.Add(new SqliteParameter("$format", format));
        }

        if (template != null)
        {
            setClauses.Add("template = $template");
            parameters.Add(new SqliteParameter("$template", template));
        }

        var sql = $"UPDATE webhooks SET {string.Join(", ", setClauses)} WHERE id = $id AND user_id = $userId";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync();

        // Fetch the updated record
        await using var fetchCmd = connection.CreateCommand();
        fetchCmd.CommandText = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE id = $id";
        fetchCmd.Parameters.AddWithValue("$id", webhookId.ToString());

        await using var reader = await fetchCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapWebhook(reader);
    }

    public async Task<Webhook?> GetWebhookAsync(Guid webhookId, string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE id = $id AND user_id = $userId";
        cmd.Parameters.AddWithValue("$id", webhookId.ToString());
        cmd.Parameters.AddWithValue("$userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapWebhook(reader);
    }

    public async Task<bool> DeleteWebhookAsync(Guid webhookId, string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM webhooks WHERE id = $id AND user_id = $userId";
        cmd.Parameters.AddWithValue("$id", webhookId.ToString());
        cmd.Parameters.AddWithValue("$userId", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IEnumerable<Webhook>> GetActiveWebhooksForEventAsync(string eventType)
    {
        var webhooks = new List<Webhook>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, url, secret, events, is_active, created_at, updated_at, format, template FROM webhooks WHERE is_active = 1";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var webhook = MapWebhook(reader);
            if (webhook.Events.Contains(eventType))
            {
                webhooks.Add(webhook);
            }
        }

        return webhooks;
    }

    private static Webhook MapWebhook(SqliteDataReader reader)
    {
        var eventsJson = reader.GetString(4);
        var events = JsonSerializer.Deserialize<string[]>(eventsJson) ?? [];

        return new Webhook
        {
            Id = Guid.Parse(reader.GetString(0)),
            UserId = reader.GetString(1),
            Url = reader.GetString(2),
            Secret = reader.IsDBNull(3) ? null : reader.GetString(3),
            Events = events,
            IsActive = reader.GetInt32(5) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            UpdatedAt = DateTime.Parse(reader.GetString(7)),
            Format = reader.GetString(8),
            Template = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }
}
