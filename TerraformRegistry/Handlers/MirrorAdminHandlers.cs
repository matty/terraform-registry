using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class MirrorAdminHandlers
{
    public static async Task<IResult> GetSummary(
        IProviderMirrorRepository providerRepository,
        IModuleMirrorRepository moduleRepository,
        HttpContext context)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        var providers = await providerRepository.ListProviderPackagesAsync(null, null, 1000, 0);
        var modules = await moduleRepository.ListModulePackagesAsync(null, null, 1000, 0);

        return Results.Ok(new MirrorAdminSummaryResponse(
            Summarize(providers.Select(x => (x.State, x.LastSyncAt))),
            Summarize(modules.Select(x => (x.State, x.LastSyncAt))),
            Summarize(
                providers.Select(x => (x.State, x.LastSyncAt))
                    .Concat(modules.Select(x => (x.State, x.LastSyncAt))))));
    }

    public static async Task<IResult> GetConfig(
        IMirrorConfigService configService,
        HttpContext context)
    {
        if (!Has(context, Permissions.MirrorConfigure)) return Forbidden();

        return Results.Ok(await configService.GetConfigAsync(context.RequestAborted));
    }

    public static async Task<IResult> UpdateConfig(
        IMirrorConfigService configService,
        IAuditService auditService,
        HttpContext context,
        HttpRequest request)
    {
        if (!Has(context, Permissions.MirrorConfigure)) return Forbidden();

        var body = await request.ReadFromJsonAsync<MirrorConfigUpdateRequest>(cancellationToken: context.RequestAborted);
        if (body is null) return Results.BadRequest(new { error = "Request body is required" });

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var config = await configService.UpdateConfigAsync(body, actor, context.RequestAborted);
        context.FireAuditLog(auditService, "mirror.config_updated", "mirror", "config", new
        {
            body.Enabled,
            ProvidersEnabled = body.Providers.Enabled,
            ModulesEnabled = body.Modules.Enabled
        });

        return Results.Ok(config);
    }

    public static async Task<IResult> ListEntries(
        IProviderMirrorRepository providerRepository,
        IModuleMirrorRepository moduleRepository,
        HttpContext context,
        string? kind,
        string? q,
        string? state,
        int limit = 50,
        int offset = 0)
    {
        if (!Has(context, Permissions.MirrorRead)) return Forbidden();

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var boundedOffset = Math.Max(0, offset);
        var includeProviders = string.IsNullOrWhiteSpace(kind) || string.Equals(kind, "provider", StringComparison.OrdinalIgnoreCase);
        var includeModules = string.IsNullOrWhiteSpace(kind) || string.Equals(kind, "module", StringComparison.OrdinalIgnoreCase);
        if (!includeProviders && !includeModules)
        {
            return Results.BadRequest(new { error = "kind must be provider or module" });
        }

        var providers = includeProviders
            ? await providerRepository.ListProviderPackagesAsync(q, state, boundedLimit, boundedOffset)
            : [];
        var modules = includeModules
            ? await moduleRepository.ListModulePackagesAsync(q, state, boundedLimit, boundedOffset)
            : [];

        return Results.Ok(new MirrorAdminEntriesResponse(providers, modules, boundedLimit, boundedOffset));
    }

    public static async Task<IResult> Retry(
        IProviderMirrorRepository providerRepository,
        IModuleMirrorRepository moduleRepository,
        IAuditService auditService,
        HttpContext context,
        HttpRequest request)
    {
        if (!Has(context, Permissions.MirrorManage)) return Forbidden();

        var body = await request.ReadFromJsonAsync<MirrorAdminRetryRequest>(cancellationToken: context.RequestAborted);
        if (body is null) return Results.BadRequest(new { error = "Request body is required" });

        var retried = await RetryCacheEntryAsync(providerRepository, moduleRepository, body);
        if (!retried) return Results.NotFound(new { error = "Mirror cache entry not found" });

        context.FireAuditLog(auditService, "mirror.retry_requested", "mirror", CacheEntryId(body), body);
        return Results.Accepted("/api/admin/mirror/entries", new { retried = true });
    }

    public static async Task<IResult> DeleteProvider(
        string hostname,
        string @namespace,
        string type,
        string version,
        IProviderMirrorRepository providerRepository,
        IAuditService auditService,
        HttpContext context)
    {
        if (!Has(context, Permissions.MirrorManage)) return Forbidden();

        var deleted = await providerRepository.DeleteProviderPackageAsync(hostname, @namespace, type, version);
        if (!deleted) return Results.NotFound(new { error = "Mirror provider cache entry not found" });

        context.FireAuditLog(auditService, "mirror.provider_deleted", "mirror_provider",
            $"{hostname}/{@namespace}/{type}/{version}", new { hostname, @namespace, type, version });
        return Results.NoContent();
    }

    public static async Task<IResult> DeleteModule(
        string hostname,
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleMirrorRepository moduleRepository,
        IAuditService auditService,
        HttpContext context)
    {
        if (!Has(context, Permissions.MirrorManage)) return Forbidden();

        var deleted = await moduleRepository.DeleteModulePackageAsync(hostname, @namespace, name, provider, version);
        if (!deleted) return Results.NotFound(new { error = "Mirror module cache entry not found" });

        context.FireAuditLog(auditService, "mirror.module_deleted", "mirror_module",
            $"{hostname}/{@namespace}/{name}/{provider}/{version}", new { hostname, @namespace, name, provider, version });
        return Results.NoContent();
    }

    private static async Task<bool> RetryCacheEntryAsync(
        IProviderMirrorRepository providerRepository,
        IModuleMirrorRepository moduleRepository,
        MirrorAdminRetryRequest request)
    {
        if (string.Equals(request.Kind, "provider", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Type)) return false;

            return await providerRepository.RetryProviderPackageAsync(
                request.Hostname,
                request.Namespace,
                request.Type,
                request.Version);
        }

        if (string.Equals(request.Kind, "module", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Provider)) return false;

            return await moduleRepository.RetryModulePackageAsync(
                request.Hostname,
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
        }

        return false;
    }

    private static MirrorAdminCacheSummary Summarize(IEnumerable<(string State, DateTime? LastSyncAt)> entries)
    {
        var items = entries.ToArray();
        var lastSyncAt = items.Length == 0 ? null : items.Max(x => x.LastSyncAt);
        return new MirrorAdminCacheSummary(
            items.Length,
            items.Count(x => string.Equals(x.State, "ready", StringComparison.OrdinalIgnoreCase)),
            items.Count(x => string.Equals(x.State, "pending", StringComparison.OrdinalIgnoreCase)),
            items.Count(x => string.Equals(x.State, "failed", StringComparison.OrdinalIgnoreCase)),
            lastSyncAt);
    }

    private static string CacheEntryId(MirrorAdminRetryRequest request) =>
        string.Equals(request.Kind, "provider", StringComparison.OrdinalIgnoreCase)
            ? $"{request.Hostname}/{request.Namespace}/{request.Type}/{request.Version}"
            : $"{request.Hostname}/{request.Namespace}/{request.Name}/{request.Provider}/{request.Version}";

    private static bool Has(HttpContext context, string permission)
    {
        return context.User.Identity?.IsAuthenticated != true || context.User.HasPermission(permission);
    }

    private static IResult Forbidden()
    {
        return Results.Content(
            """{"error":"Insufficient permissions"}""",
            "application/json",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
