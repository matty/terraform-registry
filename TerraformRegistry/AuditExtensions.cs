using System.Security.Claims;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry;

public static class AuditExtensions
{
    public static Task FireAuditLogAsync(this HttpContext context, IAuditService auditService,
        string action, string resourceType, string? resourceId = null, object? details = null)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ip = context.Connection.RemoteIpAddress?.ToString();
        return auditService.LogAsync(userId, action, resourceType, resourceId, details, ip);
    }
}
