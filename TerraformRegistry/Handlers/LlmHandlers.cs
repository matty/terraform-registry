using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class LlmHandlers
{
    public static IResult GetGuide(IConfiguration configuration)
    {
        var baseUrl = GetBaseUrl(configuration);
        var text = $$"""
Terraform Registry LLM Guide

Use authenticated JSON endpoints with:
Authorization: Bearer <token>

Discovery flow:
1. List modules: {{baseUrl}}/v1/llm/modules
2. Inspect module versions: {{baseUrl}}/v1/llm/modules/{namespace}/{name}/{provider}
3. Fetch version context: {{baseUrl}}/v1/llm/modules/{namespace}/{name}/{provider}/{version}

The version context endpoint returns the canonical machine-readable module summary for agent workflows.
Prefer it over scraping HTML pages.

Human-oriented browsing:
- Registry home: {{baseUrl}}/
- Module pages: {{baseUrl}}/modules/{namespace}/{name}/{provider}
""";

        return Results.Text(text, "text/plain");
    }

    public static async Task<IResult> ListModules(
        IModuleService moduleService,
        IConfiguration configuration,
        HttpContext context,
        string? q = null,
        int offset = 0,
        int limit = 50)
    {
        var denied = RequireAuthenticatedModuleRead(context);
        if (denied != null) return denied;

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var request = new ModuleSearchRequest
        {
            Q = q,
            Offset = Math.Max(0, offset),
            Limit = boundedLimit
        };

        var modules = await moduleService.ListModulesAsync(request);
        var baseUrl = GetBaseUrl(configuration);
        var items = modules.Modules.Select(module => new ModuleLlmIndexItem
        {
            Namespace = module.Namespace,
            Name = module.Name,
            Provider = module.Provider,
            Description = module.Description,
            LatestVersion = module.Version,
            VersionsUrl = BuildModuleVersionsUrl(baseUrl, module.Namespace, module.Name, module.Provider),
            ContextUrl = BuildModuleContextUrl(baseUrl, module.Namespace, module.Name, module.Provider, module.Version)
        }).ToList();

        return Results.Ok(new ModuleLlmIndexResponse
        {
            Registry = new ModuleLlmRegistryInfo
            {
                BaseUrl = baseUrl
            },
            Modules = items,
            Pagination = new ModuleLlmPagination
            {
                Offset = Math.Max(0, offset),
                Limit = boundedLimit,
                Returned = items.Count,
                Next = items.Count == boundedLimit
                    ? BuildModulesIndexUrl(baseUrl, q, Math.Max(0, offset) + boundedLimit, boundedLimit)
                    : null
            }
        });
    }

    public static async Task<IResult> GetModuleVersions(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService,
        IDatabaseService databaseService,
        IConfiguration configuration,
        HttpContext context)
    {
        var denied = RequireAuthenticatedModuleRead(context);
        if (denied != null) return denied;

        var versions = await moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        var versionList = versions.Modules.FirstOrDefault()?.Versions;
        if (versionList == null || versionList.Count == 0)
            return Results.NotFound(new { error = "Module not found" });

        var baseUrl = GetBaseUrl(configuration);
        var items = new List<ModuleLlmVersionItem>();

        foreach (var version in versionList)
        {
            var contextDocument = await databaseService.GetModuleLlmContextAsync(
                @namespace,
                name,
                provider,
                version.Version);

            items.Add(new ModuleLlmVersionItem
            {
                Version = version.Version,
                LlmReady = contextDocument != null,
                ContextUrl = BuildModuleContextUrl(baseUrl, @namespace, name, provider, version.Version)
            });
        }

        return Results.Ok(new ModuleLlmModuleVersionsResponse
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = @namespace,
                Name = name,
                Provider = provider
            },
            Versions = items
        });
    }

    public static async Task<IResult> GetModuleContext(
        string @namespace,
        string name,
        string provider,
        string version,
        IDatabaseService databaseService,
        HttpContext context)
    {
        var denied = RequireAuthenticatedModuleRead(context);
        if (denied != null) return denied;

        var module = await databaseService.GetModuleAsync(@namespace, name, provider, version);
        if (module == null)
            return Results.NotFound(new { error = "Module not found" });

        var llmContext = await databaseService.GetModuleLlmContextAsync(@namespace, name, provider, version);
        if (llmContext == null)
            return Results.Json(new { error = "LLM context not generated yet" }, statusCode: StatusCodes.Status409Conflict);

        return Results.Ok(llmContext);
    }

    private static IResult? RequireAuthenticatedModuleRead(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Results.Json(new { error = "Authentication required" }, statusCode: StatusCodes.Status401Unauthorized);

        if (!context.User.HasPermission(Permissions.ModulesRead))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: StatusCodes.Status403Forbidden);

        return null;
    }

    private static string GetBaseUrl(IConfiguration configuration)
    {
        return (configuration["BaseUrl"] ?? "http://localhost:5131").TrimEnd('/');
    }

    private static string BuildModulesIndexUrl(string baseUrl, string? q, int offset, int limit)
    {
        var parts = new List<string>
        {
            $"offset={offset}",
            $"limit={limit}"
        };

        if (!string.IsNullOrWhiteSpace(q))
            parts.Add($"q={Uri.EscapeDataString(q)}");

        return $"{baseUrl}/v1/llm/modules?{string.Join("&", parts)}";
    }

    private static string BuildModuleVersionsUrl(string baseUrl, string @namespace, string name, string provider)
    {
        return $"{baseUrl}/v1/llm/modules/{Uri.EscapeDataString(@namespace)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(provider)}";
    }

    private static string BuildModuleContextUrl(string baseUrl, string @namespace, string name, string provider,
        string version)
    {
        return $"{BuildModuleVersionsUrl(baseUrl, @namespace, name, provider)}/{Uri.EscapeDataString(version)}";
    }
}
