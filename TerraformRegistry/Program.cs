using Microsoft.Extensions.FileProviders;
using NSwag;
using NSwag.Generation.Processors.Security;
using TerraformRegistry;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Handlers;
using TerraformRegistry.Middleware;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.PostgreSQL.Migrations;
using TerraformRegistry.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("TF_REG_");

// Register MigrationManager and IInitializableDb for postgres
builder.Services.AddSingleton<MigrationManager>();
builder.Services.AddSingleton<IInitializableDb>(provider =>
{
    var db = provider.GetRequiredService<IDatabaseService>();
    return db as IInitializableDb ?? throw new InvalidOperationException("Database service does not implement IInitializableDb");
});

// Register database service using DI factory
builder.Services.AddSingleton<IDatabaseService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var loggerDb = provider.GetRequiredService<ILogger<PostgreSqlDatabaseService>>();
    var migrationManager = provider.GetRequiredService<MigrationManager>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "inmemory";
    var baseUrl = config["BaseUrl"] ?? "http://localhost:5131";

    if (string.IsNullOrEmpty(baseUrl))
    {
        throw new InvalidOperationException("BaseUrl is missing or empty. Please check your configuration.");
    }
    switch (databaseProvider)
    {
        case "postgres":
            var connectionString = config["PostgreSQL:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "PostgreSQL connection string is missing or empty. Please check your configuration.");
            }
            return new PostgreSqlDatabaseService(connectionString, baseUrl, loggerDb, migrationManager);
        case "inmemory":
            return new InMemoryDatabaseService(baseUrl);
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
            return new AzureBlobModuleService(config, db, provider.GetRequiredService<ILogger<AzureBlobModuleService>>());
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

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = AppJsonSerializerContext.Default;
});

bool enableSwagger = false;
var enableSwaggerConfig = builder.Configuration["EnableSwagger"];
if (!string.IsNullOrEmpty(enableSwaggerConfig) && bool.TryParse(enableSwaggerConfig, out var parsed))
{
    enableSwagger = parsed;
}
else if (builder.Environment.IsDevelopment())
{
    enableSwagger = true;
}

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
logger.LogInformation("Using {DatabaseProvider} database for module metadata", config["DatabaseProvider"] ?? "inmemory");
logger.LogInformation("Using {StorageProvider} storage for module storage", config["StorageProvider"] ?? "local");

var authToken = app.Configuration["AuthorizationToken"];
if (string.IsNullOrEmpty(authToken))
{
    throw new InvalidOperationException("AuthorizationToken is missing or empty. Please set a secure token in your configuration.");
}
if (authToken == "default-auth-token")
{
    logger.LogWarning("WARNING: The default AuthorizationToken is in use. This is not secure. Please set a secure token in your configuration.");
}

app.UseHttpsRedirection();

var webFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "web");
if (Directory.Exists(webFolderPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webFolderPath),
        RequestPath = ""
    });
}

app.UseMiddleware<AuthenticationMiddleware>(authToken);

if (enableSwagger)
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

app.MapGet("/", async (HttpContext context) =>
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

app.MapGet("/.well-known/terraform.json", ServiceDiscoveryHandlers.GetServiceDiscovery)
    .WithTags("Service Discovery")
    .WithDescription("Terraform service discovery endpoint")
    .Produces<ServiceDiscovery>();

app.MapGet("/v1/modules",
        (IModuleService moduleService, string? q, string? @namespace, string? provider, int offset, int limit) =>
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
            string provider, string version, IModuleService moduleService, HttpContext context) =>
        ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService, context))
    .WithTags("Modules")
    .WithDescription("Downloads a specific module version")
    .Produces(200, contentType: "application/zip")
    .ProducesProblem(404);

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/download",
    (string @namespace, string name, string provider, IModuleService moduleService, HttpContext context) =>
        ModuleHandlers.DownloadLatestModule(@namespace, name, provider, moduleService, context))
    .WithTags("Modules")
    .WithDescription("Downloads the latest version of a module for a provider")
    .Produces(302)
    .ProducesProblem(404);

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name,
            string provider, string version, HttpRequest request, IModuleService moduleService) =>
        await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, moduleService))
    .WithTags("Modules")
    .WithDescription("Uploads a new module version")
    .Accepts<IFormFile>("multipart/form-data")
    .ProducesProblem(400)
    .ProducesProblem(409)
    .Produces(201);

app.MapGet("/module/download", async (HttpContext context) =>
{
    var token = context.Request.Query["token"].ToString();
    if (string.IsNullOrEmpty(token) || !LocalModuleService.TryGetFilePathFromToken(token, out var filePath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Invalid or expired download link.");
        return;
    }
    if (!System.IO.File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("File not found.");
        return;
    }
    context.Response.ContentType = "application/zip";
    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(filePath)}\"";
    await context.Response.SendFileAsync(filePath);
});

app.MapFallback(async (HttpContext context) =>
{
    // Serve index.html only at root
    if (context.Request.Path == "/")
    {
        var indexPath = Path.Combine(webFolderPath, "index.html");
        if (File.Exists(indexPath))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(indexPath);
            return;
        }
    }

    // If path starts with /v1/, return problem JSON
    if (context.Request.Path.StartsWithSegments("/v1"))
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

    // All other paths: 404
    context.Response.StatusCode = 404;
});

app.Run();

public partial class Program;