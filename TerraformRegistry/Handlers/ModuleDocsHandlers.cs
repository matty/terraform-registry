using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Handlers;

public static class ModuleDocsHandlers
{
    public static async Task<IResult> GetSummary(
        IDatabaseService databaseService,
        IModuleExtractionConfigService configService,
        HttpContext context)
    {
        if (!Has(context, Permissions.ModuleDocsRead)) return Forbidden();

        var summary = await databaseService.GetModuleExtractionAdminSummaryAsync();
        var config = await configService.GetAsync(context.RequestAborted);
        return Results.Ok(new { config, summary });
    }

    public static async Task<IResult> ListModules(
        IDatabaseService databaseService,
        HttpContext context,
        string? status,
        string? q,
        int limit = 50,
        int offset = 0)
    {
        if (!Has(context, Permissions.ModuleDocsRead)) return Forbidden();

        var page = await databaseService.ListModuleExtractionsAdminAsync(new ModuleExtractionAdminQuery
        {
            Status = status,
            Q = q,
            Limit = Math.Clamp(limit, 1, 100),
            Offset = Math.Max(0, offset)
        });

        return Results.Ok(page);
    }

    public static async Task<IResult> GetModuleDetail(
        string @namespace,
        string name,
        string provider,
        string version,
        IDatabaseService databaseService,
        HttpContext context)
    {
        if (!Has(context, Permissions.ModuleDocsRead)) return Forbidden();

        var detail = await databaseService.GetModuleExtractionAdminDetailAsync(@namespace, name, provider, version);
        return detail == null ? Results.NotFound(new { error = "Module not found" }) : Results.Ok(detail);
    }

    public static async Task<IResult> Requeue(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleExtractionService extractionService,
        IDatabaseService databaseService,
        IAuditService auditService,
        IModuleExtractionConfigService configService,
        HttpContext context)
    {
        if (!Has(context, Permissions.ModuleDocsManage)) return Forbidden();
        if (!await configService.IsEnabledAsync(context.RequestAborted))
            return Conflict("Module extraction is disabled.");

        var detail = await databaseService.GetModuleExtractionAdminDetailAsync(@namespace, name, provider, version);
        if (detail == null) return Results.NotFound(new { error = "Module not found" });

        await databaseService.UpdateModuleMetadataAsync(@namespace, name, provider, version, metadata =>
        {
            metadata.Extraction ??= new ModuleExtractionState();
            metadata.Extraction.Status = "pending";
            metadata.Extraction.LastUpdatedAt = DateTime.UtcNow;
            metadata.Extraction.Error = null;
        });

        var queued = await extractionService.QueueAsync(
            new ModuleExtractionRequest(@namespace, name, provider, version),
            context.RequestAborted);

        context.FireAuditLog(auditService, "module_docs.requeued", "module",
            $"{@namespace}/{name}/{provider}/{version}", new { queued });

        return Results.Accepted($"/api/admin/module-docs/modules/{@namespace}/{name}/{provider}/{version}",
            new { queued });
    }

    public static async Task<IResult> Backfill(
        IModuleExtractionService extractionService,
        IModuleExtractionConfigService configService,
        IAuditService auditService,
        HttpContext context,
        HttpRequest request)
    {
        if (!Has(context, Permissions.ModuleDocsManage)) return Forbidden();
        if (!await configService.IsEnabledAsync(context.RequestAborted))
            return Conflict("Module extraction is disabled.");

        var body = request.ContentLength is > 0
            ? await request.ReadFromJsonAsync<BackfillRequest>(cancellationToken: context.RequestAborted)
            : null;
        var limit = Math.Clamp(body?.Limit ?? 25, 1, 100);
        var queued = await extractionService.QueueBackfillAsync(limit, context.RequestAborted);

        context.FireAuditLog(auditService, "module_docs.backfill_queued", "module_docs",
            null, new { requestedLimit = limit, queued = queued.Count });

        return Results.Accepted("/api/admin/module-docs", new { queued = queued.Count, modules = queued });
    }

    public static async Task<IResult> GetConfig(
        IModuleExtractionConfigService configService,
        HttpContext context)
    {
        if (!Has(context, Permissions.ModuleDocsConfigure)) return Forbidden();

        return Results.Ok(await configService.GetAsync(context.RequestAborted));
    }

    public static async Task<IResult> UpdateConfig(
        IModuleExtractionConfigService configService,
        IAuditService auditService,
        HttpContext context,
        HttpRequest request)
    {
        if (!Has(context, Permissions.ModuleDocsConfigure)) return Forbidden();

        var body = await request.ReadFromJsonAsync<UpdateConfigRequest>(cancellationToken: context.RequestAborted);
        if (body == null) return Results.BadRequest(new { error = "Request body is required" });

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var config = await configService.SetEnabledAsync(body.Enabled, actor, context.RequestAborted);
        context.FireAuditLog(auditService, "module_docs.config_updated", "module_docs",
            "module_extraction", new { body.Enabled });

        return Results.Ok(config);
    }

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

    private static IResult Conflict(string message)
    {
        return Results.Content(
            $$"""{"error":"{{message}}"}""",
            "application/json",
            statusCode: StatusCodes.Status409Conflict);
    }

    private sealed record BackfillRequest(int? Limit);
    private sealed record UpdateConfigRequest(bool Enabled);
}
