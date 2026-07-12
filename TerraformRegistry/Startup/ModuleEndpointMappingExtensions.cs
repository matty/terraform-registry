using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Publishing;
using Microsoft.Extensions.Options;

namespace TerraformRegistry.Startup;

internal static class ModuleEndpointMappingExtensions
{
    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        app.MapModuleRegistryEndpoints();
        app.MapLlmEndpoints();
        app.MapModuleManagementEndpoints();
        app.MapModuleDownloadEndpoint();

        return app;
    }

    private static WebApplication MapModuleRegistryEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/modules",
                (IModuleService moduleService, HttpContext context, string? q, string? @namespace, string? provider,
                        int offset = 0, int limit = 10) =>
                    ModuleHandlers.ListModules(moduleService, context, q, @namespace, provider, offset, limit))
            .WithTags("Modules")
            .WithDescription("Lists or searches modules")
            .Produces<ModuleList>();

        app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}",
                (string @namespace, string name, string provider, string version, IModuleService moduleService,
                        IModuleMirrorService moduleMirrorService, HttpContext context) =>
                    ModuleHandlers.GetModule(@namespace, name, provider, version, moduleService, moduleMirrorService, context))
            .WithTags("Modules")
            .WithDescription("Gets a specific module")
            .Produces<TerraformModule>()
            .ProducesProblem(404);

        app.MapGet("/v1/modules/{namespace}/{name}/{provider}/versions",
                (string @namespace, string name, string provider, IModuleService moduleService,
                        IModuleMirrorService moduleMirrorService, HttpContext context) =>
                    ModuleHandlers.GetModuleVersions(@namespace, name, provider, moduleService, moduleMirrorService, context))
            .WithTags("Modules")
            .WithDescription("Gets all versions of a specific module")
            .Produces<ModuleVersions>();

        app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}/download",
                (string @namespace, string name, string provider, string version, IModuleService moduleService,
                        IModuleMirrorService moduleMirrorService, IDatabaseService dbService, HttpContext context) =>
                    ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService, moduleMirrorService, dbService,
                        context))
            .WithTags("Modules")
            .WithDescription("Downloads a specific module version")
            .Produces(200, contentType: "application/zip")
            .ProducesProblem(404);

        app.MapGet("/v1/modules/{namespace}/{name}/{provider}/download",
                (string @namespace, string name, string provider, IModuleService moduleService,
                        IModuleMirrorService moduleMirrorService, IDatabaseService dbService, HttpContext context) =>
                    ModuleHandlers.DownloadLatestModule(@namespace, name, provider, moduleService, moduleMirrorService, dbService, context))
            .WithTags("Modules")
            .WithDescription("Downloads the latest version of a module for a provider")
            .Produces(302)
            .ProducesProblem(404);

        return app;
    }

    private static WebApplication MapLlmEndpoints(this WebApplication app)
    {
        app.MapGet("/llm.txt", (IConfiguration configuration) => LlmHandlers.GetGuide(configuration))
            .WithTags("LLM")
            .WithDescription("Provides LLM discovery guidance");

        app.MapGet("/v1/llm/modules",
                (IModuleService moduleService, IConfiguration configuration, HttpContext context, string? q,
                        int offset = 0, int limit = 50) =>
                    LlmHandlers.ListModules(moduleService, configuration, context, q, offset, limit))
            .WithTags("LLM")
            .WithDescription("Lists modules for authenticated LLM discovery")
            .Produces<ModuleLlmIndexResponse>();

        app.MapGet("/v1/llm/modules/{namespace}/{name}/{provider}",
                (string @namespace, string name, string provider, IModuleService moduleService,
                        IDatabaseService dbService, IConfiguration configuration, HttpContext context) =>
                    LlmHandlers.GetModuleVersions(@namespace, name, provider, moduleService, dbService, configuration,
                        context))
            .WithTags("LLM")
            .WithDescription("Lists module versions and LLM readiness for authenticated clients")
            .Produces<ModuleLlmModuleVersionsResponse>();

        app.MapGet("/v1/llm/modules/{namespace}/{name}/{provider}/{version}",
                (string @namespace, string name, string provider, string version, IDatabaseService dbService,
                        HttpContext context) =>
                    LlmHandlers.GetModuleContext(@namespace, name, provider, version, dbService, context))
            .WithTags("LLM")
            .WithDescription("Returns the stored LLM context for a module version")
            .Produces<ModuleLlmContextDocument>()
            .ProducesProblem(409);

        return app;
    }

    private static WebApplication MapModuleManagementEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}",
                async (string @namespace, string name, string provider, string version, HttpRequest request,
                        IModulePublishCoordinator publishCoordinator,
                        IOptions<ModuleExtractionOptions> extractionOptions,
                        HttpContext context) =>
                    await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, publishCoordinator,
                        extractionOptions, context))
            .WithTags("Modules")
            .WithDescription("Uploads a new module version")
            .Accepts<IFormFile>("multipart/form-data")
            .ProducesProblem(400)
            .ProducesProblem(409)
            .Produces(201);

        app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}",
                (string @namespace, string name, string provider, string version, IModuleService moduleService,
                        WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
                    ModuleHandlers.DeleteModuleVersion(@namespace, name, provider, version, moduleService,
                        webhookDispatcher, auditService, context))
            .WithTags("Modules")
            .WithDescription("Soft deletes a module version")
            .Produces(204)
            .ProducesProblem(404);

        app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}/restore",
                (string @namespace, string name, string provider, string version, IModuleService moduleService,
                        WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
                    ModuleHandlers.RestoreModuleVersion(@namespace, name, provider, version, moduleService,
                        webhookDispatcher, auditService, context))
            .WithTags("Modules")
            .WithDescription("Restores a soft-deleted module version")
            .Produces(204)
            .ProducesProblem(404);

        app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}/purge",
                (string @namespace, string name, string provider, string version, IModuleService moduleService,
                        WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
                    ModuleHandlers.PurgeModuleVersion(@namespace, name, provider, version, moduleService,
                        webhookDispatcher, auditService, context))
            .WithTags("Modules")
            .WithDescription("Permanently deletes a module version")
            .Produces(204)
            .ProducesProblem(404);

        app.MapGet("/v1/modules/trash",
                (IModuleService moduleService, HttpContext context, string? q, string? @namespace, string? provider,
                        int offset = 0, int limit = 10) =>
                    ModuleHandlers.ListDeletedModules(moduleService, context, q, @namespace, provider, offset, limit))
            .WithTags("Modules")
            .WithDescription("Lists all soft-deleted modules")
            .Produces<ModuleList>();

        app.MapPatch("/v1/modules/{namespace}/{name}/{provider}/description",
                (string @namespace, string name, string provider, HttpRequest request, IModuleService moduleService,
                        IAuditService auditService, HttpContext context) =>
                    ModuleHandlers.UpdateDescription(@namespace, name, provider, request, moduleService, auditService,
                        context))
            .WithTags("Modules")
            .WithDescription("Updates the description for a module")
            .Produces(200)
            .ProducesProblem(404);

        return app;
    }

    private static WebApplication MapModuleDownloadEndpoint(this WebApplication app)
    {
        app.MapGet("/module/download", async context =>
        {
            var token = context.Request.Query["token"].ToString();
            var moduleService = context.RequestServices.GetRequiredService<IModuleService>() as LocalModuleService;
            if (string.IsNullOrEmpty(token) || moduleService is null || !moduleService.TryGetFilePathFromToken(token, out var filePath))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Invalid or expired download link.");
                return;
            }

            if (!File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("File not found.");
                return;
            }

            context.Response.ContentType = filePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                                           filePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
                ? "application/gzip"
                : "application/zip";
            context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(filePath)}\"";
            await context.Response.SendFileAsync(filePath);
        });

        return app;
    }
}
