using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Handlers;

public static class MirrorAdminHandlers
{
    public static async Task<IResult> PurgeProvider(
        MirrorCacheBudgetService cacheBudget,
        IAuditService auditService,
        HttpContext context,
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch)
    {
        if (!Has(context, Permissions.MirrorManage)) return Forbidden();

        var result = await cacheBudget.PurgeProviderAsync(
            hostname, providerNamespace, type, version, os, arch, context.RequestAborted);
        if (result == MirrorCachePurgeResult.InUse)
        {
            return Results.Conflict(new { error = "The mirror cache entry is currently in use." });
        }
        if (result == MirrorCachePurgeResult.NotFound)
        {
            return Results.NotFound(new { error = "Mirror cache entry not found." });
        }
        if (result == MirrorCachePurgeResult.Failed)
        {
            return Results.Problem("The mirror cache entry could not be purged.", statusCode: StatusCodes.Status502BadGateway);
        }

        await context.FireAuditLogAsync(auditService, "mirror.provider_purged", "mirror_provider",
            $"{hostname}/{providerNamespace}/{type}/{version}/{os}/{arch}");
        return Results.NoContent();
    }

    public static async Task<IResult> PurgeModule(
        MirrorCacheBudgetService cacheBudget,
        IAuditService auditService,
        HttpContext context,
        string hostname,
        string moduleNamespace,
        string name,
        string provider,
        string version)
    {
        if (!Has(context, Permissions.MirrorManage)) return Forbidden();

        var result = await cacheBudget.PurgeModuleAsync(
            hostname, moduleNamespace, name, provider, version, context.RequestAborted);
        if (result == MirrorCachePurgeResult.InUse)
        {
            return Results.Conflict(new { error = "The mirror cache entry is currently in use." });
        }
        if (result == MirrorCachePurgeResult.NotFound)
        {
            return Results.NotFound(new { error = "Mirror cache entry not found." });
        }
        if (result == MirrorCachePurgeResult.Failed)
        {
            return Results.Problem("The mirror cache entry could not be purged.", statusCode: StatusCodes.Status502BadGateway);
        }

        await context.FireAuditLogAsync(auditService, "mirror.module_purged", "mirror_module",
            $"{hostname}/{moduleNamespace}/{name}/{provider}/{version}");
        return Results.NoContent();
    }

    public static async Task<IResult> ListProviderCache(
        IProviderMirrorRepository providerRepository,
        HttpContext context,
        string? q,
        string? state,
        int limit = 50,
        int offset = 0)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        var packages = await providerRepository.ListProviderPackagesAsync(
            q, state, Math.Clamp(limit, 1, 100), Math.Max(0, offset));
        return Results.Ok(packages);
    }

    public static async Task<IResult> ListModuleCache(
        IModuleMirrorRepository moduleRepository,
        HttpContext context,
        string? q,
        string? state,
        int limit = 50,
        int offset = 0)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        var packages = await moduleRepository.ListModulePackagesAsync(
            q, state, Math.Clamp(limit, 1, 100), Math.Max(0, offset));
        return Results.Ok(packages);
    }

    public static async Task<IResult> ListLeases(
        IMirrorLeaseRepository leaseRepository,
        HttpContext context,
        int limit = 50,
        int offset = 0)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        var leases = await leaseRepository.ListLeasesAsync(limit, offset, context.RequestAborted);
        return Results.Ok(leases);
    }

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
        await context.FireAuditLogAsync(auditService, "mirror.config_updated", "mirror", "config", new
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
