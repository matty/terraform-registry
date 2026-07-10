using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
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

app.UseHttpsRedirection();

// Add global exception handling middleware early in the pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

var webFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "web");
if (Directory.Exists(webFolderPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webFolderPath),
        RequestPath = ""
    });
}

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

public partial class Program;
