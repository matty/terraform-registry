using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class MirrorAdminHandlers
{
    public static async Task<IResult> GetConfig(IMirrorConfigService configService, HttpContext context)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        return Results.Ok(await configService.GetConfigAsync(context.RequestAborted));
    }

    public static async Task<IResult> UpdateConfig(
        IMirrorConfigService configService,
        IAuditService auditService,
        HttpContext context,
        MirrorConfigUpdateRequest request)
    {
        if (!Has(context, Permissions.MirrorConfigure)) return Forbidden();

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var response = await configService.UpdateConfigAsync(request, actor, context.RequestAborted);
        context.FireAuditLog(auditService, "mirror.config_updated", "mirror", "config", new
        {
            request.Enabled,
            ProvidersEnabled = request.Providers.Enabled,
            ModulesEnabled = request.Modules.Enabled,
            request.Limits.MaxConcurrentDownloads,
            request.Limits.MaxTotalCachedBytes
        });
        return Results.Ok(response);
    }

    private static bool Has(HttpContext context, string permission) =>
        context.User.Identity?.IsAuthenticated != true || context.User.HasPermission(permission);

    private static IResult Forbidden() => Results.Content(
        """{\"error\":\"Insufficient permissions\"}""",
        "application/json",
        statusCode: StatusCodes.Status403Forbidden);
}
