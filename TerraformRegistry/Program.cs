using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using System.Net;
using NSwag;
using NSwag.Generation.Processors.Security;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Handlers;
using TerraformRegistry.Middleware;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.S3;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Services.Publishing;
using TerraformRegistry.Startup;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
    .AddEnvironmentVariables("TF_REG_");

builder.Services.AddTerraformRegistryServices(builder.Configuration);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
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
RegistryLog.Information(logger, "Using {DatabaseProvider} database for module metadata",
    config["DatabaseProvider"] ?? "sqlite");
RegistryLog.Information(logger, "Using {StorageProvider} storage for module storage", config["StorageProvider"] ?? "local");

var authToken = app.Configuration["AuthorizationToken"];
if (string.IsNullOrEmpty(authToken))
{
    throw new InvalidOperationException(
        "AuthorizationToken is missing or empty. Please set a secure token in your configuration.");
}

if (authToken == "default-auth-token"
    && !app.Environment.IsDevelopment()
    && !app.Environment.IsEnvironment("Test"))
{
    throw new InvalidOperationException(
        "AuthorizationToken is set to the default placeholder value. Configure a unique secret before running outside Development/Test.");
}

var trustedProxies = app.Configuration["TrustedProxies"];
if (!string.IsNullOrWhiteSpace(trustedProxies))
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    foreach (var value in trustedProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException($"TrustedProxies contains an invalid IP address: '{value}'.");
        forwardedHeaders.KnownProxies.Add(address);
    }
    app.UseForwardedHeaders(forwardedHeaders);
}

app.UseHttpsRedirection();

// Add global exception handling middleware early in the pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseResponseCompression();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});

var webFolderPath = Path.Combine(app.Environment.ContentRootPath, "web");
if (Directory.Exists(webFolderPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webFolderPath),
        RequestPath = "",
        OnPrepareResponse = context =>
        {
            if (IsFingerprintedFrontendAsset(context.Context.Request.Path))
                context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
    });
}

// Portal authentication middleware (validates JWT sessions for portal routes)
var jwtService = app.Services.GetRequiredService<JwtService>();
var apiKeySecurityOptions = app.Services.GetRequiredService<ApiKeySecurityOptions>();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test") &&
    apiKeySecurityOptions.DigestKey == "configure-a-unique-api-key-digest-key-before-production")
{
    throw new InvalidOperationException(
        "ApiKeySecurity:DigestKey is set to the default placeholder. Configure a unique secret before running outside Development/Test.");
}
apiKeySecurityOptions.ValidateDigestKey();
var artifactDownloadSigningKey = app.Configuration["ArtifactDownloadTokens:SigningKey"];
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test") &&
    artifactDownloadSigningKey == ArtifactDownloadTokenService.ProductionPlaceholder)
{
    throw new InvalidOperationException(
        "ArtifactDownloadTokens:SigningKey is set to the default placeholder. Configure a unique secret before running outside Development/Test.");
}
_ = app.Services.GetRequiredService<ArtifactDownloadTokenService>();
app.UseMiddleware<PortalAuthenticationMiddleware>(jwtService);

// API key authentication middleware (for /v1/* routes used by Terraform CLI)
// Supports both static token and JWT session authentication
app.UseMiddleware<AuthenticationMiddleware>(authToken, jwtService);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (enableSwagger)
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

var port = builder.Configuration["PORT"] ?? builder.Configuration["Port"] ?? "5131";
if (!int.TryParse(port, out var portNumber))
    throw new InvalidOperationException($"Invalid port specified: '{port}'. Please check your configuration.");

app.MapCoreEndpoints(webFolderPath, jwtService);
app.MapProviderEndpoints();
app.MapAdminEndpoints();
app.MapVcsEndpoints();
app.MapModuleEndpoints();
app.MapMirrorEndpoints();

app.MapControllers();

app.MapFallback(async context =>
{
    // If path starts with /v1/, /api/, or /mirror/, return problem JSON (API routes)
    if (context.Request.Path.StartsWithSegments("/v1") ||
        context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/mirror"))
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
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
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

static bool IsFingerprintedFrontendAsset(PathString requestPath)
{
    if (!requestPath.StartsWithSegments("/_nuxt") && !requestPath.StartsWithSegments("/_fonts"))
        return false;

    var fingerprint = Path.GetFileNameWithoutExtension(requestPath.Value ?? string.Empty);
    return fingerprint.Length >= 8 && fingerprint.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public partial class Program;
