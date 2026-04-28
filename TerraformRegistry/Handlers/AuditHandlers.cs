using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class AuditHandlers
{
    public static async Task<IResult> ListAuditLogs(
        IAuditService auditService, HttpContext context,
        string? action = null, string? userId = null, string? resourceType = null,
        DateTime? from = null, DateTime? to = null, int limit = 50, int offset = 0)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AdminAudit))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await auditService.QueryAsync(action, userId, resourceType, from, to, limit, offset);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetAuditLog(Guid id, IAuditService auditService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AdminAudit))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var entry = await auditService.GetAsync(id);
        return entry != null ? Results.Ok(entry) : Results.NotFound();
    }
}
