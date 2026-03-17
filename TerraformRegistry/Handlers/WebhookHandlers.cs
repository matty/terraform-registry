using System.Security.Claims;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class WebhookHandlers
{
    public static async Task<IResult> ListWebhooks(IWebhookService webhookService, HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var webhooks = await webhookService.ListWebhooksAsync(userId);
        return Results.Ok(webhooks);
    }

    public static async Task<IResult> CreateWebhook(IWebhookService webhookService, HttpContext context, HttpRequest request)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var body = await request.ReadFromJsonAsync<CreateWebhookRequest>();
        if (body == null || string.IsNullOrEmpty(body.Url) || body.Events == null || body.Events.Length == 0)
            return Results.BadRequest(new { error = "url and events are required" });
        var format = body.Format ?? "generic";
        string[] validFormats = ["generic", "discord", "slack", "teams", "custom"];
        if (!validFormats.Contains(format))
            return Results.BadRequest(new { error = $"Invalid format. Must be one of: {string.Join(", ", validFormats)}" });
        if (format == "custom" && string.IsNullOrWhiteSpace(body.Template))
            return Results.BadRequest(new { error = "Template is required when format is 'custom'" });
        var webhook = await webhookService.CreateWebhookAsync(userId, body.Url, body.Events, body.Secret, format, body.Template);
        return Results.Created($"/api/webhooks/{webhook.Id}", webhook);
    }

    public static async Task<IResult> UpdateWebhook(Guid id, IWebhookService webhookService, HttpContext context, HttpRequest request)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var body = await request.ReadFromJsonAsync<UpdateWebhookRequest>();
        var updated = await webhookService.UpdateWebhookAsync(id, userId, body?.Url, body?.Events, body?.Secret, body?.IsActive, body?.Format, body?.Template);
        if (updated == null) return Results.NotFound(new { error = "Webhook not found or access denied" });
        return Results.Ok(updated);
    }

    public static async Task<IResult> DeleteWebhook(Guid id, IWebhookService webhookService, HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var result = await webhookService.DeleteWebhookAsync(id, userId);
        return result ? Results.NoContent() : Results.NotFound(new { error = "Webhook not found or access denied" });
    }
}

public record CreateWebhookRequest(string Url, string[] Events, string? Secret, string? Format, string? Template);
public record UpdateWebhookRequest(string? Url, string[]? Events, string? Secret, bool? IsActive, string? Format, string? Template);
