using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

public static class WebhookHandlers
{
    public static async Task<IResult> ListWebhooks(IWebhookService webhookService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var webhooks = await webhookService.ListWebhooksAsync(userId);
        return Results.Ok(webhooks);
    }

    public static async Task<IResult> CreateWebhook(IWebhookService webhookService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

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
        context.FireAuditLog(auditService, "webhook.created", "webhook", webhook.Id.ToString(), new { url = body.Url, events = body.Events });

        return Results.Created($"/api/webhooks/{webhook.Id}", webhook);
    }

    public static async Task<IResult> UpdateWebhook(Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var body = await request.ReadFromJsonAsync<UpdateWebhookRequest>();
        var updated = await webhookService.UpdateWebhookAsync(id, userId, body?.Url, body?.Events, body?.Secret, body?.IsActive, body?.Format, body?.Template);
        if (updated == null) return Results.NotFound(new { error = "Webhook not found or access denied" });

        context.FireAuditLog(auditService, "webhook.updated", "webhook", id.ToString());

        return Results.Ok(updated);
    }

    public static async Task<IResult> DeleteWebhook(Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var result = await webhookService.DeleteWebhookAsync(id, userId);
        if (!result) return Results.NotFound(new { error = "Webhook not found or access denied" });

        context.FireAuditLog(auditService, "webhook.deleted", "webhook", id.ToString());

        return Results.NoContent();
    }

    public static async Task<IResult> TestWebhook(Guid id, IWebhookService webhookService, WebhookDispatcher dispatcher, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var webhook = await webhookService.GetWebhookAsync(id, userId);
        if (webhook == null) return Results.NotFound(new { error = "Webhook not found or access denied" });

        var (success, error) = await dispatcher.SendTestAsync(webhook);
        return success
            ? Results.Ok(new { message = "Test webhook delivered successfully" })
            : Results.Json(new { error = $"Test delivery failed: {error}" }, statusCode: 502);
    }
}

public record CreateWebhookRequest(string Url, string[] Events, string? Secret, string? Format, string? Template);
public record UpdateWebhookRequest(string? Url, string[]? Events, string? Secret, bool? IsActive, string? Format, string? Template);
