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
using TerraformRegistry.PostgreSQL.Migrations;
using TerraformRegistry.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables("TF_REG_");

// Register database retry options
builder.Services.Configure<DatabaseRetryOptions>(builder.Configuration.GetSection("DatabaseRetry"));

// Register MigrationManager and IInitializableDb for database initialization
builder.Services.AddSingleton<MigrationManager>();
builder.Services.AddSingleton<IInitializableDb>(provider =>
{
    var db = provider.GetRequiredService<IDatabaseService>();
    return db as IInitializableDb ??
           throw new InvalidOperationException("Database service does not implement IInitializableDb");
});

// Register database service using DI factory
builder.Services.AddSingleton<IDatabaseService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var loggerDb = provider.GetRequiredService<ILogger<PostgreSqlDatabaseService>>();
    var migrationManager = provider.GetRequiredService<MigrationManager>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    var baseUrl = config["BaseUrl"] ?? "http://localhost:5131";

    if (string.IsNullOrEmpty(baseUrl))
        throw new InvalidOperationException("BaseUrl is missing or empty. Please check your configuration.");
    switch (databaseProvider)
    {
        case "postgres":
            var connectionString = config["PostgreSQL:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
                throw new InvalidOperationException(
                    "PostgreSQL connection string is missing or empty. Please check your configuration.");
            return new PostgreSqlDatabaseService(connectionString, baseUrl, loggerDb, migrationManager);
        case "sqlite":
            var sqliteConn = config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db";
            var sqliteLogger = provider.GetRequiredService<ILogger<SqliteDatabaseService>>();
            return new SqliteDatabaseService(sqliteConn, baseUrl, sqliteLogger);
        default:
            throw new Exception($"Invalid database provider specified: '{databaseProvider}'. Check configuration.");
    }
});

// Register module storage service using DI factory
builder.Services.AddSingleton<IModuleService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var db = provider.GetRequiredService<IDatabaseService>();
    var logger = provider.GetRequiredService<ILogger<LocalModuleService>>();
    var storageProvider = config["StorageProvider"]?.ToLower() ?? "local";
    switch (storageProvider)
    {
        case "azure":
            return new AzureBlobModuleService(config, db,
                provider.GetRequiredService<ILogger<AzureBlobModuleService>>());
        case "local":
            var storagePath = config["ModuleStoragePath"];
            if (string.IsNullOrEmpty(storagePath))
            {
                logger.LogError(
                    "ModuleStoragePath is missing or empty. Please check your configuration. Application cannot start.");
                throw new InvalidOperationException(
                    "ModuleStoragePath is missing or empty. Please check your configuration.");
            }

            return new LocalModuleService(config, db, logger);
        default:
            throw new Exception($"Invalid storage provider specified: '{storageProvider}'. Check configuration.");
    }
});

builder.Services.AddHostedService<DatabaseInitializerHostedService>();

// Register HttpClientFactory for OAuth flows
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("WebhookDelivery", c => c.Timeout = TimeSpan.FromSeconds(5));

// Register Controllers (for ApiKeyController)
builder.Services.AddControllers();

// Register Authentication (required for [Authorize] attribute)
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "CustomBearer";
        options.DefaultChallengeScheme = "CustomBearer";
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        TerraformRegistry.Middleware.CustomBearerHandler>("CustomBearer", options => { });

// Register OIDC configuration and services
var oidcOptions = new OidcOptions();
builder.Configuration.GetSection("Oidc").Bind(oidcOptions);
builder.Services.AddSingleton(oidcOptions);
builder.Services.AddSingleton<JwtService>();

builder.Services.AddSingleton<OAuthService>();

// Register API Key Service
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();

// Register Analytics Service
builder.Services.AddSingleton<IAnalyticsService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlAnalyticsService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for analytics service.")),
        "sqlite" => new SqliteAnalyticsService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

// Register Webhook Service
builder.Services.AddSingleton<IWebhookService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlWebhookService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for webhook service.")),
        "sqlite" => new SqliteWebhookService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});
builder.Services.AddSingleton<WebhookDispatcher>();

// Register VCS Source Service
builder.Services.AddSingleton<IVcsSourceService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlVcsSourceService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for VCS source service.")),
        "sqlite" => new SqliteVcsSourceService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

builder.Services.AddSingleton<GitHubVcsService>();
builder.Services.AddHttpClient("GitHubVcs", c => c.Timeout = TimeSpan.FromSeconds(60));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Add other options as needed
});

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
if (authToken == "default-auth-token")
    logger.LogWarning(
        "WARNING: The default AuthorizationToken is in use. This is not secure. Please set a secure token in your configuration.");

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

app.MapGet("/", async context =>
{
    var indexPath = Path.Combine(webFolderPath, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    }
    else
    {
        context.Response.StatusCode = 404;
    }
});

// OIDC Authentication endpoints
var oauthService = app.Services.GetRequiredService<OAuthService>();

app.MapGet("/api/auth/providers", () => AuthHandlers.GetProviders(oauthService))
    .WithTags("Authentication")
    .WithDescription("Returns list of enabled OIDC providers");

app.MapGet("/api/auth/login/{provider}", (string provider, HttpContext context) =>
        AuthHandlers.Login(provider, oauthService, context))
    .WithTags("Authentication")
    .WithDescription("Initiates OIDC login flow for the specified provider");

app.MapGet("/api/auth/callback/{provider}", async (string provider, string? code, string? state, string? error,
            HttpContext context, IApiKeyService apiKeyService, ILogger<Program> authLogger) =>
        await AuthHandlers.Callback(provider, code, state, error, oauthService, jwtService, apiKeyService, context,
            authLogger))
    .WithTags("Authentication")
    .WithDescription("Handles OIDC callback after provider authentication");

app.MapGet("/api/auth/me", (HttpContext context) => AuthHandlers.GetCurrentUser(jwtService, context))
    .WithTags("Authentication")
    .WithDescription("Returns current user info from session");

app.MapPost("/api/auth/logout", (HttpContext context) => AuthHandlers.Logout(context))
    .WithTags("Authentication")
    .WithDescription("Logs out the current user");

app.MapDelete("/api/auth/me", (HttpContext context, IApiKeyService apiKeyService, IDatabaseService dbService) =>
        AuthHandlers.DeleteAccount(context, apiKeyService, dbService))
    .WithTags("Authentication")
    .WithDescription("Deletes the current user account")
    .RequireAuthorization();

app.MapGet("/api/auth/session", (HttpContext context) => AuthHandlers.CheckSession(jwtService, context))
    .WithTags("Authentication")
    .WithDescription("Checks if user has a valid session");

// Dev-only login endpoint — not registered in production (route doesn't exist)
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/auth/dev-login",
            (JwtService jwt, IConfiguration cfg, IHostEnvironment env, HttpContext ctx) =>
                AuthHandlers.DevLogin(jwt, cfg, env, ctx))
        .WithTags("Authentication")
        .WithDescription("Creates a dev session (Development only, requires TF_REG_DevAuthBypass=true)");
}

app.MapGet("/.well-known/terraform.json", ServiceDiscoveryHandlers.GetServiceDiscovery)
    .WithTags("Service Discovery")
    .WithDescription("Terraform service discovery endpoint")
    .Produces<ServiceDiscovery>();

app.MapGet("/health", HealthHandlers.HandleHealth)
    .WithTags("Health")
    .WithDescription("Liveness probe")
    .Produces(200);

app.MapGet("/ready", (IDatabaseService dbService, IModuleService moduleService, HttpContext context, IConfiguration config) =>
        HealthHandlers.HandleReady(dbService, moduleService, context, config))
    .WithTags("Health")
    .WithDescription("Readiness probe — use ?detail=true with auth for component details")
    .Produces(200)
    .Produces(503);

// Analytics endpoints (auth handled by middleware via /api/analytics prefix)
app.MapGet("/api/analytics/downloads/summary", (IAnalyticsService analytics) =>
        AnalyticsHandlers.GetSummary(analytics))
    .WithTags("Analytics")
    .WithDescription("Download summary statistics");

app.MapGet("/api/analytics/downloads/top", (IAnalyticsService analytics, int limit = 10, string period = "30d") =>
        AnalyticsHandlers.GetTopModules(analytics, limit, period))
    .WithTags("Analytics")
    .WithDescription("Top downloaded modules");

app.MapGet("/api/analytics/downloads/trends", (IAnalyticsService analytics, string period = "30d", string interval = "day") =>
        AnalyticsHandlers.GetTrends(analytics, period, interval))
    .WithTags("Analytics")
    .WithDescription("Download trends over time");

app.MapGet("/api/analytics/downloads/module/{namespace}/{name}/{provider}",
        (string @namespace, string name, string provider, IAnalyticsService analytics, string period = "30d") =>
            AnalyticsHandlers.GetModuleAnalytics(@namespace, name, provider, analytics, period))
    .WithTags("Analytics")
    .WithDescription("Per-module download analytics");

// Webhook endpoints (auth handled by middleware via /api/webhooks prefix)
app.MapGet("/api/webhooks", (IWebhookService webhookService, HttpContext context) =>
        WebhookHandlers.ListWebhooks(webhookService, context))
    .WithTags("Webhooks");

app.MapPost("/api/webhooks", (IWebhookService webhookService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.CreateWebhook(webhookService, context, request))
    .WithTags("Webhooks");

app.MapPut("/api/webhooks/{id}", (Guid id, IWebhookService webhookService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.UpdateWebhook(id, webhookService, context, request))
    .WithTags("Webhooks");

app.MapDelete("/api/webhooks/{id}", (Guid id, IWebhookService webhookService, HttpContext context) =>
        WebhookHandlers.DeleteWebhook(id, webhookService, context))
    .WithTags("Webhooks");

app.MapPost("/api/webhooks/{id}/test", (Guid id, IWebhookService webhookService, WebhookDispatcher dispatcher, HttpContext context) =>
        WebhookHandlers.TestWebhook(id, webhookService, dispatcher, context))
    .WithTags("Webhooks");

// VCS source CRUD endpoints (auth handled by middleware via /api/vcs/sources prefix)
app.MapGet("/api/vcs/sources", (IVcsSourceService vcsService, HttpContext context) =>
        VcsHandlers.ListVcsSources(vcsService, context))
    .WithTags("VCS");

app.MapPost("/api/vcs/sources", (IVcsSourceService vcsService, IConfiguration config, HttpContext context, HttpRequest request) =>
        VcsHandlers.CreateVcsSource(vcsService, config, context, request))
    .WithTags("VCS");

app.MapPut("/api/vcs/sources/{id}", (Guid id, IVcsSourceService vcsService, IConfiguration config, HttpContext context, HttpRequest request) =>
        VcsHandlers.UpdateVcsSource(id, vcsService, config, context, request))
    .WithTags("VCS");

app.MapDelete("/api/vcs/sources/{id}", (Guid id, IVcsSourceService vcsService, HttpContext context) =>
        VcsHandlers.DeleteVcsSource(id, vcsService, context))
    .WithTags("VCS");

// GitHub webhook endpoint (public, no auth required)
app.MapPost("/api/vcs/github/webhook", (GitHubVcsService githubService, HttpContext context) =>
        VcsHandlers.HandleGitHubWebhook(githubService, context))
    .WithTags("VCS");

app.MapGet("/v1/modules",
        (IModuleService moduleService, string? q, string? @namespace, string? provider, int offset = 0,
                int limit = 10) =>
            ModuleHandlers.ListModules(moduleService, q, @namespace, provider, offset, limit))
    .WithTags("Modules")
    .WithDescription("Lists or searches modules")
    .Produces<ModuleList>();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}", (string @namespace, string name, string provider,
            string version, IModuleService moduleService) =>
        ModuleHandlers.GetModule(@namespace, name, provider, version, moduleService))
    .WithTags("Modules")
    .WithDescription("Gets a specific module")
    .Produces<Module>()
    .ProducesProblem(404);

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/versions",
        (string @namespace, string name, string provider, IModuleService moduleService) =>
            ModuleHandlers.GetModuleVersions(@namespace, name, provider, moduleService))
    .WithTags("Modules")
    .WithDescription("Gets all versions of a specific module")
    .Produces<ModuleVersions>();

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

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name,
            string provider, string version, HttpRequest request, IModuleService moduleService, WebhookDispatcher webhookDispatcher) =>
        await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, moduleService, webhookDispatcher))
    .WithTags("Modules")
    .WithDescription("Uploads a new module version")
    .Accepts<IFormFile>("multipart/form-data")
    .ProducesProblem(400)
    .ProducesProblem(409)
    .Produces(201);

// Module version management - soft delete, restore, purge
app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher) =>
            ModuleHandlers.DeleteModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher))
    .WithTags("Modules")
    .WithDescription("Soft deletes a module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}/restore",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher) =>
            ModuleHandlers.RestoreModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher))
    .WithTags("Modules")
    .WithDescription("Restores a soft-deleted module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapDelete("/v1/modules/{namespace}/{name}/{provider}/{version}/purge",
        (string @namespace, string name, string provider, string version, IModuleService moduleService, WebhookDispatcher webhookDispatcher) =>
            ModuleHandlers.PurgeModuleVersion(@namespace, name, provider, version, moduleService, webhookDispatcher))
    .WithTags("Modules")
    .WithDescription("Permanently deletes a module version")
    .Produces(204)
    .ProducesProblem(404);

app.MapGet("/v1/modules/trash",
        (IModuleService moduleService, string? q, string? @namespace, string? provider, int offset = 0,
                int limit = 10) =>
            ModuleHandlers.ListDeletedModules(moduleService, q, @namespace, provider, offset, limit))
    .WithTags("Modules")
    .WithDescription("Lists all soft-deleted modules")
    .Produces<ModuleList>();

app.MapPatch("/v1/modules/{namespace}/{name}/{provider}/description",
        (string @namespace, string name, string provider, HttpRequest request, IModuleService moduleService) =>
            ModuleHandlers.UpdateDescription(@namespace, name, provider, request, moduleService))
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