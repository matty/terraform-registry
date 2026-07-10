using Microsoft.AspNetCore.Mvc;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Startup;

internal static class EndpointMappingExtensions
{
    public static WebApplication MapCoreEndpoints(
        this WebApplication app,
        string webFolderPath,
        JwtService jwtService)
    {
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

        app.MapAuthenticationEndpoints(jwtService);

        app.MapGet("/.well-known/terraform.json", ServiceDiscoveryHandlers.GetServiceDiscovery)
            .WithTags("Service Discovery")
            .WithDescription("Terraform service discovery endpoint")
            .Produces<ServiceDiscovery>();

        app.MapGet("/health", HealthHandlers.HandleHealth)
            .WithTags("Health")
            .WithDescription("Liveness probe")
            .Produces(200);

        app.MapGet("/ready",
                (IDatabaseService dbService, IModuleService moduleService,
                        IProviderArtifactStorage providerArtifactStorage, IStartupReadiness startupReadiness,
                        HttpContext context, IConfiguration config) =>
                    HealthHandlers.HandleReady(dbService, moduleService, providerArtifactStorage, startupReadiness, context, config))
            .WithTags("Health")
            .WithDescription("Readiness probe — use ?detail=true with auth for component details")
            .Produces(200)
            .Produces(503);

        app.MapAnalyticsEndpoints();

        return app;
    }

    private static WebApplication MapAuthenticationEndpoints(this WebApplication app, JwtService jwtService)
    {
        var oauthService = app.Services.GetRequiredService<OAuthService>();

        app.MapGet("/api/auth/providers", () => AuthHandlers.GetProviders(oauthService))
            .WithTags("Authentication")
            .WithDescription("Returns list of enabled OIDC providers");

        app.MapGet("/api/auth/login/{provider}", (string provider, string? returnTo, HttpContext context) =>
                AuthHandlers.Login(provider, returnTo, oauthService, context))
            .WithTags("Authentication")
            .WithDescription("Initiates OIDC login flow for the specified provider");

        app.MapGet("/api/auth/callback/{provider}",
                async (string provider, string? code, string? state, string? error,
                        HttpContext context, IApiKeyService apiKeyService, IAuditService auditService,
                        ILogger<Program> authLogger) =>
                    await AuthHandlers.Callback(provider, code, state, error, oauthService, jwtService, apiKeyService,
                        auditService, context, authLogger))
            .WithTags("Authentication")
            .WithDescription("Handles OIDC callback after provider authentication");

        app.MapGet("/api/auth/me", async (HttpContext context, IPermissionService permService) =>
            {
                return await AuthHandlers.GetCurrentUser(jwtService, permService, context);
            })
            .WithTags("Authentication")
            .WithDescription("Returns current user info from session");

        app.MapPost("/api/auth/logout", (HttpContext context, IAuditService auditService) =>
                AuthHandlers.Logout(context, auditService))
            .WithTags("Authentication")
            .WithDescription("Logs out the current user");

        app.MapDelete("/api/auth/me",
                (HttpContext context, IApiKeyService apiKeyService, IDatabaseService dbService,
                        IAuditService auditService) =>
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

        app.MapPost("/api/auth/terraform/token",
                (HttpContext context, ITerraformAuthorizationCodeStore codeStore, IApiKeyService apiKeyService,
                        IAuditService auditService) =>
                    AuthHandlers.ExchangeTerraformToken(context, codeStore, apiKeyService, auditService))
            .WithTags("Authentication")
            .WithDescription("Exchanges a Terraform CLI OAuth authorization code for an API token");

        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/api/auth/dev-login",
                    (JwtService jwt, IConfiguration cfg, IHostEnvironment env, IAuditService auditService,
                            HttpContext ctx) =>
                        AuthHandlers.DevLogin(jwt, cfg, env, auditService, ctx))
                .WithTags("Authentication")
                .WithDescription("Creates a dev session (Development only, requires TF_REG_DevAuthBypass=true)");
        }

        return app;
    }

    private static WebApplication MapAnalyticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/analytics/downloads/summary", (IAnalyticsService analytics, HttpContext context) =>
                AnalyticsHandlers.GetSummary(analytics, context))
            .WithTags("Analytics")
            .WithDescription("Download summary statistics");

        app.MapGet("/api/analytics/downloads/top",
                (IAnalyticsService analytics, HttpContext context, int limit = 10, string period = "30d") =>
                    AnalyticsHandlers.GetTopModules(analytics, context, limit, period))
            .WithTags("Analytics")
            .WithDescription("Top downloaded modules");

        app.MapGet("/api/analytics/downloads/trends",
                (IAnalyticsService analytics, HttpContext context, string period = "30d", string interval = "day") =>
                    AnalyticsHandlers.GetTrends(analytics, context, period, interval))
            .WithTags("Analytics")
            .WithDescription("Download trends over time");

        app.MapGet("/api/analytics/downloads/module/{namespace}/{name}/{provider}",
                (string @namespace, string name, string provider, IAnalyticsService analytics, HttpContext context,
                        string period = "30d") =>
                    AnalyticsHandlers.GetModuleAnalytics(@namespace, name, provider, analytics, context, period))
            .WithTags("Analytics")
            .WithDescription("Per-module download analytics");

        return app;
    }
}
