using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using NSwag;
using NSwag.Generation.Processors.Security;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Handlers;
using TerraformRegistry.Middleware;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.Migrations;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Services.Publishing;
using TerraformRegistry.Startup;
using TerraformRegistry.S3;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables("TF_REG_");

builder.Services.AddTerraformRegistryServices(builder.Configuration);

var enableSwagger = false;
var enableSwaggerConfig = builder.Configuration["EnableSwagger"];
if (!string.IsNullOrEmpty(enableSwaggerConfig) && bool.TryParse(enableSwaggerConfig, out var parsed))
    enableSwagger = parsed;
else if (builder.Environment.IsDevelopment()) enableSwagger = true;

if (enableSwagger)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApiDocument(options =>
    {
        options.Title = "Terraform Registry API";
        options.Version = "v1";
        options.Description = "A private Terraform Registry API for modules";
        // Add Bearer authentication support
        options.AddSecurity("Bearer", new OpenApiSecurityScheme
        {
            Type = OpenApiSecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Name = "Authorization",
            In = OpenApiSecurityApiKeyLocation.Header,
            Description = "Enter your Bearer token in the format: Bearer {token}"
        });
        options.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("Bearer"));
    });
}

var app = builder.Build();

// Log which providers are in use
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var config = app.Services.GetRequiredService<IConfiguration>();
logger.LogInformation("Using {DatabaseProvider} database for module metadata",
    config["DatabaseProvider"] ?? "sqlite");
logger.LogInformation("Using {StorageProvider} storage for module storage", config["StorageProvider"] ?? "local");

var authToken = app.Configuration["AuthorizationToken"];
if (string.IsNullOrEmpty(authToken))
    throw new InvalidOperationException(
        "AuthorizationToken is missing or empty. Please set a secure token in your configuration.");
if (authToken == "default-auth-token"
    && !app.Environment.IsDevelopment()
    && !app.Environment.IsEnvironment("Test"))
    throw new InvalidOperationException(
        "AuthorizationToken is set to the default placeholder value. Configure a unique secret before running outside Development/Test.");

app.Services.GetRequiredService<IModuleService>();

app.UseHttpsRedirection();

// Add global exception handling middleware early in the pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

var webFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "web");
if (Directory.Exists(webFolderPath))
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webFolderPath),
        RequestPath = ""
    });

// Portal authentication middleware (validates JWT sessions for portal routes)
var jwtService = app.Services.GetRequiredService<JwtService>();
app.UseMiddleware<PortalAuthenticationMiddleware>(jwtService);

// API key authentication middleware (for /v1/* routes used by Terraform CLI)
// Supports both static token and JWT session authentication
app.UseMiddleware<AuthenticationMiddleware>(authToken, jwtService);

app.UseAuthentication();
app.UseAuthorization();

if (enableSwagger)
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

var port = builder.Configuration["PORT"] ?? builder.Configuration["Port"] ?? "5131";
if (!int.TryParse(port, out var portNumber))
    throw new InvalidOperationException($"Invalid port specified: '{port}'. Please check your configuration.");

app.MapCoreEndpoints(webFolderPath, jwtService);

// Provider management endpoints (auth handled by middleware via /api/providers prefix)
app.MapGet("/api/providers", (IProviderRegistryService service, HttpContext context, string? q, int offset = 0, int limit = 20) =>
        ProviderHandlers.ListProviders(service, context, q, offset, limit))
    .WithTags("Providers");

app.MapPost("/api/providers", (IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.CreateProvider(service, context, request))
    .WithTags("Providers");

app.MapGet("/api/providers/{namespace}/{type}", (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.GetProvider(@namespace, type, service, context))
    .WithTags("Providers");

app.MapPatch("/api/providers/{namespace}/{type}", (string @namespace, string type, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.UpdateProvider(@namespace, type, service, context, request))
    .WithTags("Providers");

app.MapDelete("/api/providers/{namespace}/{type}", (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.DeleteProvider(@namespace, type, service, context))
    .WithTags("Providers");

app.MapGet("/api/providers/{namespace}/{type}/gpg-keys", (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.ListGpgKeys(@namespace, type, service, context))
    .WithTags("Providers");

app.MapPost("/api/providers/{namespace}/{type}/gpg-keys", (string @namespace, string type, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.AddGpgKey(@namespace, type, service, context, request))
    .WithTags("Providers");

app.MapDelete("/api/providers/{namespace}/{type}/gpg-keys/{keyId}", (string @namespace, string type, string keyId, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.RevokeGpgKey(@namespace, type, keyId, service, context))
    .WithTags("Providers");

app.MapGet("/api/providers/{namespace}/{type}/versions", (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.ListVersions(@namespace, type, service, context))
    .WithTags("Providers");

app.MapPost("/api/providers/{namespace}/{type}/versions", (string @namespace, string type, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.CreateVersion(@namespace, type, service, context, request))
    .WithTags("Providers");

app.MapDelete("/api/providers/{namespace}/{type}/versions/{version}", (string @namespace, string type, string version, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.DeleteVersion(@namespace, type, version, service, context))
    .WithTags("Providers");

app.MapGet("/api/providers/{namespace}/{type}/versions/{version}/platforms", (string @namespace, string type, string version, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.ListPlatforms(@namespace, type, version, service, context))
    .WithTags("Providers");

app.MapPost("/api/providers/{namespace}/{type}/versions/{version}/platforms", (string @namespace, string type, string version, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.CreatePlatform(@namespace, type, version, service, context, request))
    .WithTags("Providers");

app.MapDelete("/api/providers/{namespace}/{type}/versions/{version}/platforms/{os}/{arch}", (string @namespace, string type, string version, string os, string arch, IProviderRegistryService service, HttpContext context) =>
        ProviderHandlers.DeletePlatform(@namespace, type, version, os, arch, service, context))
    .WithTags("Providers");

app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/shasums", (string @namespace, string type, string version, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.UploadShasums(@namespace, type, version, service, context, request))
    .WithTags("Providers");

app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/shasums.sig", (string @namespace, string type, string version, IProviderRegistryService service, HttpContext context, HttpRequest request) =>
        ProviderHandlers.UploadShasumsSignature(@namespace, type, version, service, context, request))
    .WithTags("Providers");

app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/platforms/{os}/{arch}/package",
        (string @namespace, string type, string version, string os, string arch, IProviderRegistryService service, HttpContext context,
                HttpRequest request) =>
            ProviderHandlers.UploadPlatformPackage(@namespace, type, version, os, arch, service, context, request))
    .WithTags("Providers");

// Webhook endpoints (admin-only, auth handled by middleware via /api/admin prefix)
app.MapGet("/api/admin/webhooks", (IWebhookService webhookService, HttpContext context) =>
        WebhookHandlers.ListWebhooks(webhookService, context))
    .WithTags("Webhooks");

app.MapPost("/api/admin/webhooks", (IWebhookService webhookService, IAuditService auditService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.CreateWebhook(webhookService, auditService, context, request))
    .WithTags("Webhooks");

app.MapPut("/api/admin/webhooks/{id}", (Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.UpdateWebhook(id, webhookService, auditService, context, request))
    .WithTags("Webhooks");

app.MapDelete("/api/admin/webhooks/{id}", (Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context) =>
        WebhookHandlers.DeleteWebhook(id, webhookService, auditService, context))
    .WithTags("Webhooks");

app.MapPost("/api/admin/webhooks/{id}/test", (Guid id, IWebhookService webhookService, WebhookDispatcher dispatcher, HttpContext context) =>
        WebhookHandlers.TestWebhook(id, webhookService, dispatcher, context))
    .WithTags("Webhooks");

// Admin - Roles
app.MapGet("/api/admin/roles", (IRoleService roleService, HttpContext context) => AdminHandlers.ListRoles(roleService, context)).WithTags("Admin");
app.MapPost("/api/admin/roles", (IRoleService roleService, IAuditService auditService, HttpContext context, HttpRequest request) => AdminHandlers.CreateRole(roleService, auditService, context, request)).WithTags("Admin");
app.MapPut("/api/admin/roles/{id}", (Guid id, IRoleService roleService, IAuditService auditService, HttpContext context, HttpRequest request) => AdminHandlers.UpdateRole(id, roleService, auditService, context, request)).WithTags("Admin");
app.MapDelete("/api/admin/roles/{id}", (Guid id, IRoleService roleService, IAuditService auditService, HttpContext context) => AdminHandlers.DeleteRole(id, roleService, auditService, context)).WithTags("Admin");

// Admin - Audit
app.MapGet("/api/admin/audit", (IAuditService auditService, HttpContext context, string? action, string? userId, string? resourceType, DateTime? from, DateTime? to, int limit = 50, int offset = 0) =>
    AuditHandlers.ListAuditLogs(auditService, context, action, userId, resourceType, from, to, limit, offset)).WithTags("Admin");
app.MapGet("/api/admin/audit/{id}", (Guid id, IAuditService auditService, HttpContext context) =>
    AuditHandlers.GetAuditLog(id, auditService, context)).WithTags("Admin");

// Admin - Users
app.MapGet("/api/admin/users", (IDatabaseService dbService, IPermissionService permService, HttpContext context) => AdminHandlers.ListUsers(dbService, permService, context)).WithTags("Admin");
app.MapGet("/api/admin/users/{userId}/roles", (string userId, IPermissionService permService, HttpContext context) => AdminHandlers.GetUserRoles(userId, permService, context)).WithTags("Admin");
app.MapPost("/api/admin/users/{userId}/roles", (string userId, IPermissionService permService, IAuditService auditService, HttpContext context, HttpRequest request) => AdminHandlers.AssignUserRole(userId, permService, auditService, context, request)).WithTags("Admin");
app.MapDelete("/api/admin/users/{userId}/roles/{roleId}", (string userId, Guid roleId, IPermissionService permService, IRoleService roleService, IAuditService auditService, HttpContext context) => AdminHandlers.RemoveUserRole(userId, roleId, permService, roleService, auditService, context)).WithTags("Admin");

// Admin - Module Docs
app.MapGet("/api/admin/module-docs/summary",
        (IDatabaseService dbService, IModuleExtractionConfigService configService, HttpContext context) =>
            ModuleDocsHandlers.GetSummary(dbService, configService, context))
    .WithTags("Module Docs");
app.MapGet("/api/admin/module-docs/modules",
        (IDatabaseService dbService, HttpContext context, string? status, string? q, int limit = 50, int offset = 0) =>
            ModuleDocsHandlers.ListModules(dbService, context, status, q, limit, offset))
    .WithTags("Module Docs");
app.MapGet("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}",
        (string @namespace, string name, string provider, string version, IDatabaseService dbService, HttpContext context) =>
            ModuleDocsHandlers.GetModuleDetail(@namespace, name, provider, version, dbService, context))
    .WithTags("Module Docs");
app.MapPost("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}/regenerate-llm",
        (string @namespace, string name, string provider, string version, IModuleExtractionService extractionService,
                IDatabaseService dbService, IAuditService auditService, IModuleExtractionConfigService configService,
                HttpContext context) =>
            ModuleDocsHandlers.RegenerateLlmContext(@namespace, name, provider, version, extractionService, dbService,
                auditService, configService, context))
    .WithTags("Module Docs");
app.MapPost("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}/requeue",
        (string @namespace, string name, string provider, string version, IModuleExtractionService extractionService,
                IDatabaseService dbService, IAuditService auditService, IModuleExtractionConfigService configService,
                HttpContext context) =>
            ModuleDocsHandlers.Requeue(@namespace, name, provider, version, extractionService, dbService, auditService,
                configService, context))
    .WithTags("Module Docs");
app.MapPost("/api/admin/module-docs/backfill",
        (IModuleExtractionService extractionService, IModuleExtractionConfigService configService,
                IAuditService auditService, HttpContext context, HttpRequest request) =>
            ModuleDocsHandlers.Backfill(extractionService, configService, auditService, context, request))
    .WithTags("Module Docs");
app.MapGet("/api/admin/module-docs/config",
        (IModuleExtractionConfigService configService, HttpContext context) =>
            ModuleDocsHandlers.GetConfig(configService, context))
    .WithTags("Module Docs");
app.MapPut("/api/admin/module-docs/config",
        (IModuleExtractionConfigService configService, IAuditService auditService, HttpContext context,
                HttpRequest request) =>
            ModuleDocsHandlers.UpdateConfig(configService, auditService, context, request))
    .WithTags("Module Docs");

// VCS source CRUD endpoints (auth handled by middleware via /api/vcs/sources prefix)
app.MapGet("/api/vcs/sources", (IVcsSourceService vcsService, HttpContext context) =>
        VcsHandlers.ListVcsSources(vcsService, context))
    .WithTags("VCS");

app.MapPost("/api/vcs/sources", (IVcsSourceService vcsService, IVcsConnectionService connectionService, IGitHubVcsService githubService, IAuditService auditService, HttpContext context, HttpRequest request) =>
        VcsHandlers.CreateVcsSource(vcsService, connectionService, githubService, auditService, context, request))
    .WithTags("VCS");

app.MapGet("/api/vcs/sources/module/{namespace}/{name}/{provider}", (string @namespace, string name, string provider, IVcsSourceService vcsService, HttpContext context) =>
        VcsHandlers.GetVcsSourceByModule(vcsService, context, @namespace, name, provider))
    .WithTags("VCS");

app.MapPut("/api/vcs/sources/{id}", (Guid id, IVcsSourceService vcsService, IVcsConnectionService connectionService, IAuditService auditService, HttpContext context, HttpRequest request) =>
        VcsHandlers.UpdateVcsSource(id, vcsService, connectionService, auditService, context, request))
    .WithTags("VCS");

app.MapDelete("/api/vcs/sources/{id}", (Guid id, IVcsSourceService vcsService, IAuditService auditService, HttpContext context) =>
        VcsHandlers.DeleteVcsSource(id, vcsService, auditService, context))
    .WithTags("VCS");

app.MapPost("/api/vcs/sources/{id}/sync", (Guid id, IVcsSourceService vcsService, IGitHubVcsService githubService, HttpContext context, HttpRequest request) =>
        VcsHandlers.SyncVcsSource(id, vcsService, githubService, context, request))
    .WithTags("VCS");

// VCS Connection admin endpoints
app.MapGet("/api/admin/vcs-connections", (IVcsConnectionService connectionService, HttpContext context) =>
        VcsHandlers.ListConnections(connectionService, context))
    .WithTags("VCS");

app.MapPost("/api/admin/vcs-connections", (IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService, HttpContext context, HttpRequest request) =>
        VcsHandlers.CreateConnection(connectionService, config, auditService, context, request))
    .WithTags("VCS");

app.MapPut("/api/admin/vcs-connections/{id}", (Guid id, IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService, HttpContext context, HttpRequest request) =>
        VcsHandlers.UpdateConnection(id, connectionService, config, auditService, context, request))
    .WithTags("VCS");

app.MapDelete("/api/admin/vcs-connections/{id}", (Guid id, IVcsConnectionService connectionService, IAuditService auditService, HttpContext context) =>
        VcsHandlers.DeleteConnection(id, connectionService, auditService, context))
    .WithTags("VCS");

// Lightweight connection list for Add Module dropdown (auth required, not admin-only)
app.MapGet("/api/vcs/connections", (IVcsConnectionService connectionService, HttpContext context) =>
        VcsHandlers.ListConnectionSummaries(connectionService, context))
    .WithTags("VCS");

// GitHub webhook endpoint (public, no auth required)
app.MapPost("/api/vcs/github/webhook", (IGitHubVcsService githubService, HttpContext context) =>
        VcsHandlers.HandleGitHubWebhook(githubService, context))
    .WithTags("VCS");

app.MapGet("/v1/modules",
        (IModuleService moduleService, HttpContext context, string? q, string? @namespace, string? provider, int offset = 0,
                int limit = 10) =>
            ModuleHandlers.ListModules(moduleService, context, q, @namespace, provider, offset, limit))
    .WithTags("Modules")
    .WithDescription("Lists or searches modules")
    .Produces<ModuleList>();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}", (string @namespace, string name, string provider,
            string version, IModuleService moduleService, HttpContext context) =>
        ModuleHandlers.GetModule(@namespace, name, provider, version, moduleService, context))
    .WithTags("Modules")
    .WithDescription("Gets a specific module")
    .Produces<Module>()
    .ProducesProblem(404);

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/versions",
        (string @namespace, string name, string provider, IModuleService moduleService, HttpContext context) =>
            ModuleHandlers.GetModuleVersions(@namespace, name, provider, moduleService, context))
    .WithTags("Modules")
    .WithDescription("Gets all versions of a specific module")
    .Produces<ModuleVersions>();

app.MapGet("/llm.txt", (IConfiguration configuration) => LlmHandlers.GetGuide(configuration))
    .WithTags("LLM")
    .WithDescription("Provides LLM discovery guidance");

app.MapGet("/v1/llm/modules",
        (IModuleService moduleService, IConfiguration configuration, HttpContext context, string? q, int offset = 0,
                int limit = 50) =>
            LlmHandlers.ListModules(moduleService, configuration, context, q, offset, limit))
    .WithTags("LLM")
    .WithDescription("Lists modules for authenticated LLM discovery")
    .Produces<ModuleLlmIndexResponse>();

app.MapGet("/v1/llm/modules/{namespace}/{name}/{provider}",
        (string @namespace, string name, string provider, IModuleService moduleService, IDatabaseService dbService,
                IConfiguration configuration, HttpContext context) =>
            LlmHandlers.GetModuleVersions(@namespace, name, provider, moduleService, dbService, configuration, context))
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

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}/download", (string @namespace, string name,
            string provider, string version, IModuleService moduleService, IDatabaseService dbService, HttpContext context) =>
        ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService, dbService, context))
    .WithTags("Modules")
    .WithDescription("Downloads a specific module version")
    .Produces(200, contentType: "application/zip")
    .ProducesProblem(404);

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/download",
        (string @namespace, string name, string provider, IModuleService moduleService, IDatabaseService dbService, HttpContext context) =>
            ModuleHandlers.DownloadLatestModule(@namespace, name, provider, moduleService, dbService, context))
    .WithTags("Modules")
    .WithDescription("Downloads the latest version of a module for a provider")
    .Produces(302)
    .ProducesProblem(404);

app.MapGet("/v1/providers/{namespace}/{type}/versions",
        (string @namespace, string type, IProviderRegistryService providerService, HttpContext context) =>
            ProviderHandlers.GetVersions(@namespace, type, providerService, context))
    .WithTags("Providers")
    .WithDescription("Gets all versions for a provider")
    .Produces<ProviderVersionsResponse>()
    .ProducesProblem(404);

app.MapGet("/v1/providers/{namespace}/{type}/{version}/download/{os}/{arch}",
        (string @namespace, string type, string version, string os, string arch, IProviderRegistryService providerService,
                HttpContext context) =>
            ProviderHandlers.GetPackage(@namespace, type, version, os, arch, providerService, context))
    .WithTags("Providers")
    .WithDescription("Gets package metadata for a provider version and platform")
    .Produces<ProviderPackageResponse>()
    .ProducesProblem(404);

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name,
            string provider, string version, HttpRequest request, IModulePublishCoordinator publishCoordinator, HttpContext context) =>
        await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, publishCoordinator, context))
    .WithTags("Modules")
    .WithDescription("Uploads a new module version")
    .Accepts<IFormFile>("multipart/form-data")
    .ProducesProblem(400)
    .ProducesProblem(409)
    .Produces(201);

// Module version management - soft delete, restore, purge
app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
            ModuleHandlers.DeleteModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher, auditService, context))
    .WithTags("Modules")
    .WithDescription("Soft deletes a module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}/restore",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
            ModuleHandlers.RestoreModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher, auditService, context))
    .WithTags("Modules")
    .WithDescription("Restores a soft-deleted module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}/purge",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher, IAuditService auditService, HttpContext context) =>
            ModuleHandlers.PurgeModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher, auditService, context))
    .WithTags("Modules")
    .WithDescription("Permanently deletes a module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapGet("/v1/modules/trash",
        (IModuleService moduleService, HttpContext context, string? q, string? @namespace, string? provider, int offset = 0,
                int limit = 10) =>
            ModuleHandlers.ListDeletedModules(moduleService, context, q, @namespace, provider, offset, limit))
    .WithTags("Modules")
    .WithDescription("Lists all soft-deleted modules")
    .Produces<ModuleList>();

app.MapPatch("/v1/modules/{namespace}/{name}/{provider}/description",
        (string @namespace, string name, string provider, HttpRequest request, IModuleService moduleService, IAuditService auditService, HttpContext context) =>
            ModuleHandlers.UpdateDescription(@namespace, name, provider, request, moduleService, auditService, context))
    .WithTags("Modules")
    .WithDescription("Updates the description for a module")
    .Produces(200)
    .ProducesProblem(404);

app.MapGet("/module/download", async context =>
{
    var token = context.Request.Query["token"].ToString();
    if (string.IsNullOrEmpty(token) || !LocalModuleService.TryGetFilePathFromToken(token, out var filePath))
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

    context.Response.ContentType = "application/zip";
    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(filePath)}\"";
    await context.Response.SendFileAsync(filePath);
});

app.MapGet("/provider/download", async context =>
{
    var token = context.Request.Query["token"].ToString();
    if (string.IsNullOrEmpty(token) || !LocalProviderArtifactStorage.TryGetFilePathFromToken(token, out var filePath))
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

    context.Response.ContentType = Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
        ? "application/zip"
        : "text/plain";
    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(filePath)}\"";
    await context.Response.SendFileAsync(filePath);
});

app.MapControllers();

app.MapFallback(async context =>
{
    // If path starts with /v1/ or /api/, return problem JSON (API routes)
    if (context.Request.Path.StartsWithSegments("/v1") || context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = 404;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Type = "404",
            Title = "Not Found",
            Status = 404,
            Detail = "The requested resource was not found."
        };
        await context.Response.WriteAsJsonAsync(problem);
        return;
    }

    // For all other paths, serve index.html (SPA fallback)
    var indexPath = Path.Combine(webFolderPath, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
        return;
    }

    // If no index.html exists, return 404
    context.Response.StatusCode = 404;
});

app.Run($"http://0.0.0.0:{portNumber}");

public partial class Program;
