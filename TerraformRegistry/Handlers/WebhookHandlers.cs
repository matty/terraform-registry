using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

public static class WebhookHandlers
{
    private static readonly string[] ValidFormats = ["generic", "discord", "slack", "teams", "custom"];

    public static async Task<IResult> ListWebhooks(IWebhookService webhookService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var webhooks = await webhookService.ListAllWebhooksAsync();
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

        var validator = context.RequestServices.GetRequiredService<WebhookUrlValidator>();
        try
        {
            await validator.ValidateOutboundWebhookUrlAsync(body.Url, context.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var format = body.Format ?? "generic";
        var validationError = ValidateFormatAndTemplate(format, body.Template);
        if (validationError != null)
            return Results.BadRequest(new { error = validationError });

        var webhook = await webhookService.CreateWebhookAsync(userId, body.Url, body.Events, body.Secret, format, body.Template);
        await context.FireAuditLogAsync(auditService, "webhook.created", "webhook", webhook.Id.ToString(), new { url = body.Url, events = body.Events });

        return Results.Created($"/api/webhooks/{webhook.Id}", webhook);
    }

    public static async Task<IResult> UpdateWebhook(Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.WebhooksManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var body = await request.ReadFromJsonAsync<UpdateWebhookRequest>();
        var existing = await webhookService.GetWebhookAsync(id, userId);
        if (existing == null) return Results.NotFound(new { error = "Webhook not found or access denied" });

        if (body?.Url != null)
        {
            var validator = context.RequestServices.GetRequiredService<WebhookUrlValidator>();
            try
            {
                await validator.ValidateOutboundWebhookUrlAsync(body.Url, context.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }

        if (body?.Format != null || body?.Template != null)
        {
            var effectiveFormat = body?.Format ?? existing.Format;
            var effectiveTemplate = body?.Template ?? existing.Template;
            var validationError = ValidateFormatAndTemplate(effectiveFormat, effectiveTemplate);
            if (validationError != null)
                return Results.BadRequest(new { error = validationError });
        }

        var updated = await webhookService.UpdateWebhookAsync(id, userId, body?.Url, body?.Events, body?.Secret, body?.IsActive, body?.Format, body?.Template);
        if (updated == null) return Results.NotFound(new { error = "Webhook not found or access denied" });

        await context.FireAuditLogAsync(auditService, "webhook.updated", "webhook", id.ToString());

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

        await context.FireAuditLogAsync(auditService, "webhook.deleted", "webhook", id.ToString());

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

    private static string? ValidateFormatAndTemplate(string format, string? template)
    {
        if (!ValidFormats.Contains(format))
            return $"Invalid format. Must be one of: {string.Join(", ", ValidFormats)}";

        if (format == "custom" && string.IsNullOrWhiteSpace(template))
            return "Template is required when format is 'custom'";

        return null;
    }
}

public record CreateWebhookRequest(string Url, string[] Events, string? Secret, string? Format, string? Template);
public record UpdateWebhookRequest(string? Url, string[]? Events, string? Secret, bool? IsActive, string? Format, string? Template);
