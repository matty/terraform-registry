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

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables("TF_REG_");

// Register database retry options
builder.Services.Configure<DatabaseRetryOptions>(builder.Configuration.GetSection("DatabaseRetry"));
builder.Services.Configure<WebhookSecurityOptions>(builder.Configuration.GetSection("WebhookSecurity"));
builder.Services.AddSingleton<IWebhookHostResolver, DnsWebhookHostResolver>();
builder.Services.AddSingleton<IWebhookStreamConnector, SocketWebhookStreamConnector>();
builder.Services.AddSingleton<WebhookPinnedConnectionHelper>();

// Register DbUpMigrator and IInitializableDb for database initialization
builder.Services.AddSingleton<DbUpMigrator>();
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
    var dbUpMigrator = provider.GetRequiredService<DbUpMigrator>();
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
            return new PostgreSqlDatabaseService(connectionString, baseUrl, loggerDb, dbUpMigrator);
        case "sqlite":
            var sqliteConn = config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db";
            var sqliteLogger = provider.GetRequiredService<ILogger<SqliteDatabaseService>>();
            return new SqliteDatabaseService(sqliteConn, baseUrl, sqliteLogger, dbUpMigrator);
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
builder.Services.AddHttpClient("WebhookDelivery", c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(services => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = services.GetRequiredService<WebhookPinnedConnectionHelper>().ConnectAsync
    });

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
builder.Services.AddSingleton(new TerraformLoginOptions());
builder.Services.AddSingleton<ITerraformAuthorizationCodeStore, InMemoryTerraformAuthorizationCodeStore>();

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
builder.Services.AddSingleton<WebhookUrlValidator>();
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

// Register VCS Connection Service
builder.Services.AddSingleton<IVcsConnectionService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlVcsConnectionService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for VCS connection service.")),
        "sqlite" => new SqliteVcsConnectionService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

builder.Services.AddSingleton<IModuleExtractionService, NoOpModuleExtractionService>();
builder.Services.AddSingleton<IModulePublishCoordinator, ModulePublishCoordinator>();
builder.Services.AddSingleton<GitHubVcsService>();
builder.Services.AddSingleton<IGitHubVcsService>(provider => provider.GetRequiredService<GitHubVcsService>());
builder.Services.AddHttpClient("GitHubVcs", c => c.Timeout = TimeSpan.FromSeconds(60));

// Register Role Service
builder.Services.AddSingleton<IRoleService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlRoleService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for role service.")),
        "sqlite" => new SqliteRoleService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

// Register Permission Service
builder.Services.AddSingleton<IPermissionService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlPermissionService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for permission service.")),
        "sqlite" => new SqlitePermissionService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

// Register Audit Service
builder.Services.AddSingleton<IAuditService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new TerraformRegistry.PostgreSQL.PostgreSqlAuditService(
            config["PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("PostgreSQL connection string is missing for audit service."),
            provider.GetRequiredService<ILogger<TerraformRegistry.PostgreSQL.PostgreSqlAuditService>>()),
        "sqlite" => new SqliteAuditService(
            config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db",
            provider.GetRequiredService<ILogger<SqliteAuditService>>()),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

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
if (authToken == "default-auth-token"
    && !app.Environment.IsDevelopment()
    && !app.Environment.IsEnvironment("Test"))
    throw new InvalidOperationException(
        "AuthorizationToken is set to the default placeholder value. Configure a unique secret before running outside Development/Test.");

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

app.MapGet("/api/auth/login/{provider}", (string provider, string? returnTo, HttpContext context) =>
        AuthHandlers.Login(provider, returnTo, oauthService, context))
    .WithTags("Authentication")
    .WithDescription("Initiates OIDC login flow for the specified provider");

app.MapGet("/api/auth/callback/{provider}", async (string provider, string? code, string? state, string? error,
            HttpContext context, IApiKeyService apiKeyService, IAuditService auditService, ILogger<Program> authLogger) =>
        await AuthHandlers.Callback(provider, code, state, error, oauthService, jwtService, apiKeyService, auditService, context,
            authLogger))
    .WithTags("Authentication")
    .WithDescription("Handles OIDC callback after provider authentication");

app.MapGet("/api/auth/me", async (HttpContext context, IPermissionService permService) =>
    {
        return await AuthHandlers.GetCurrentUser(jwtService, permService, context);
    })
    .WithTags("Authentication")
    .WithDescription("Returns current user info from session");

app.MapPost("/api/auth/logout", (HttpContext context, IAuditService auditService) => AuthHandlers.Logout(context, auditService))
    .WithTags("Authentication")
    .WithDescription("Logs out the current user");

app.MapDelete("/api/auth/me", (HttpContext context, IApiKeyService apiKeyService, IDatabaseService dbService, IAuditService auditService) =>
        AuthHandlers.DeleteAccount(context, apiKeyService, dbService, auditService))
    .WithTags("Authentication")
    .WithDescription("Deletes the current user account")
    .RequireAuthorization();

app.MapGet("/api/auth/session", (HttpContext context) => AuthHandlers.CheckSession(jwtService, context))
    .WithTags("Authentication")
    .WithDescription("Checks if user has a valid session");

app.MapGet("/api/auth/terraform/authorize", (HttpContext context) =>
        AuthHandlers.BeginTerraformAuthorization(context))
    .WithTags("Authentication")
    .WithDescription("Begins Terraform CLI OAuth authorization flow");

app.MapPost("/api/auth/terraform/token", (HttpContext context, ITerraformAuthorizationCodeStore codeStore, IApiKeyService apiKeyService, IAuditService auditService) =>
        AuthHandlers.ExchangeTerraformToken(context, codeStore, apiKeyService, auditService))
    .WithTags("Authentication")
    .WithDescription("Exchanges a Terraform CLI OAuth authorization code for an API token");

// Dev-only login endpoint — not registered in production (route doesn't exist)
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/auth/dev-login",
            (JwtService jwt, IConfiguration cfg, IHostEnvironment env, IAuditService auditService, HttpContext ctx) =>
                AuthHandlers.DevLogin(jwt, cfg, env, auditService, ctx))
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
app.MapGet("/api/analytics/downloads/summary", (IAnalyticsService analytics, HttpContext context) =>
        AnalyticsHandlers.GetSummary(analytics, context))
    .WithTags("Analytics")
    .WithDescription("Download summary statistics");

app.MapGet("/api/analytics/downloads/top", (IAnalyticsService analytics, HttpContext context, int limit = 10, string period = "30d") =>
        AnalyticsHandlers.GetTopModules(analytics, context, limit, period))
    .WithTags("Analytics")
    .WithDescription("Top downloaded modules");

app.MapGet("/api/analytics/downloads/trends", (IAnalyticsService analytics, HttpContext context, string period = "30d", string interval = "day") =>
        AnalyticsHandlers.GetTrends(analytics, context, period, interval))
    .WithTags("Analytics")
    .WithDescription("Download trends over time");

app.MapGet("/api/analytics/downloads/module/{namespace}/{name}/{provider}",
        (string @namespace, string name, string provider, IAnalyticsService analytics, HttpContext context, string period = "30d") =>
            AnalyticsHandlers.GetModuleAnalytics(@namespace, name, provider, analytics, context, period))
    .WithTags("Analytics")
    .WithDescription("Per-module download analytics");

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

app.MapPost("/api/vcs/sources/{id}/sync", (Guid id, IGitHubVcsService githubService, HttpContext context, HttpRequest request) =>
        VcsHandlers.SyncVcsSource(id, githubService, context, request))
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
