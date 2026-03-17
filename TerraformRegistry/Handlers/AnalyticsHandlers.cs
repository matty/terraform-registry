using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class AnalyticsHandlers
{
    public static async Task<IResult> GetSummary(IAnalyticsService analyticsService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AnalyticsView))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var summary = await analyticsService.GetDownloadSummaryAsync();
        return Results.Ok(summary);
    }

    public static async Task<IResult> GetTopModules(IAnalyticsService analyticsService, HttpContext context, int limit = 10, string period = "30d")
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AnalyticsView))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await analyticsService.GetTopModulesAsync(limit, period);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetTrends(IAnalyticsService analyticsService, HttpContext context, string period = "30d", string interval = "day")
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AnalyticsView))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await analyticsService.GetDownloadTrendsAsync(period, interval);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetModuleAnalytics(
        string @namespace, string name, string provider,
        IAnalyticsService analyticsService, HttpContext context, string period = "30d")
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.AnalyticsView))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await analyticsService.GetModuleAnalyticsAsync(@namespace, name, provider, period);
        if (result == null) return Results.NotFound(new { error = "No download data for this module" });
        return Results.Ok(result);
    }
}
