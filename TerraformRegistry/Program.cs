using TerraformRegistry;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("TF_REG_");

var databaseProvider = builder.Configuration["DatabaseProvider"]?.ToLower() ?? "inmemory";

switch (databaseProvider)
{
   case "postgres":
      builder.Services.AddSingleton<TerraformRegistry.PostgreSQL.Migrations.MigrationManager>();
      builder.Services.AddSingleton<IDatabaseService>(provider =>
      {
         var config = provider.GetRequiredService<IConfiguration>();
         var loggerDb = provider.GetRequiredService<ILogger<TerraformRegistry.PostgreSQL.PostgreSqlDatabaseService>>();
         var migrationManager = provider.GetRequiredService<TerraformRegistry.PostgreSQL.Migrations.MigrationManager>();
         var connectionString = config["PostgreSQL:ConnectionString"];
         if (string.IsNullOrEmpty(connectionString))
         {
            throw new InvalidOperationException("PostgreSQL connection string is missing or empty. Please check your configuration.");
         }
         var baseUrl = config["BaseUrl"] ?? "http://localhost:5131";
         if (string.IsNullOrEmpty(baseUrl))
         {
            throw new InvalidOperationException("BaseUrl is missing or empty. Please check your configuration.");
         }
         return new TerraformRegistry.PostgreSQL.PostgreSqlDatabaseService(connectionString, baseUrl, loggerDb, migrationManager);
      });
      builder.Services.AddSingleton(provider => (IInitializableDb)provider.GetRequiredService<IDatabaseService>());
      break;

   case "inmemory":
      builder.Services.AddSingleton<IDatabaseService>(provider =>
      {
         var config = provider.GetRequiredService<IConfiguration>();
         var baseUrl = config["BaseUrl"] ?? "http://localhost:5131";
         if (string.IsNullOrEmpty(baseUrl))
         {
            throw new InvalidOperationException("BaseUrl is missing or empty. Please check your configuration.");
         }
         return new InMemoryDatabaseService(baseUrl);
      });
      break;

   default:
      throw new Exception($"Invalid database provider specified: '{databaseProvider}'. Check configuration.");
}

var storageProvider = builder.Configuration["StorageProvider"]?.ToLower() ?? "local";

switch (storageProvider)
{
   case "azure":
      builder.Services.AddSingleton<IModuleService, TerraformRegistry.AzureBlob.AzureBlobModuleService>();
      break;

   case "local":
      builder.Services.AddSingleton<IModuleService>(provider =>
      {
         var config = provider.GetRequiredService<IConfiguration>();
         var db = provider.GetRequiredService<IDatabaseService>();
         var logger = provider.GetRequiredService<ILogger<LocalModuleService>>();
         var storagePath = config["ModuleStoragePath"];
         if (string.IsNullOrEmpty(storagePath))
         {
            logger.LogError("ModuleStoragePath is missing or empty. Please check your configuration. Application cannot start.");
            throw new InvalidOperationException("ModuleStoragePath is missing or empty. Please check your configuration.");
         }
         return new LocalModuleService(config, db, logger);
      });
      break;

   default:
      throw new Exception($"Invalid storage provider specified: '{storageProvider}'. Check configuration.");
}

// Register the database initializer hosted service
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
      options.AddSecurity("Bearer", new NSwag.OpenApiSecurityScheme
      {
         Type = NSwag.OpenApiSecuritySchemeType.Http,
         Scheme = "bearer",
         BearerFormat = "JWT",
         Name = "Authorization",
         In = NSwag.OpenApiSecurityApiKeyLocation.Header,
         Description = "Enter your Bearer token in the format: Bearer {token}"
      });
      options.OperationProcessors.Add(new NSwag.Generation.Processors.Security.AspNetCoreOperationSecurityScopeProcessor("Bearer"));
   });
}

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Using {DatabaseProvider} database for module metadata", databaseProvider);
logger.LogInformation("Using {StorageProvider} storage for module storage", storageProvider);

var authToken = app.Configuration["AuthorizationToken"];

if (authToken == "default-auth-token")
{
   logger.LogWarning("WARNING: The default AuthorizationToken is in use. This is not secure. Please set a secure token in your configuration.");
}

if (!string.IsNullOrEmpty(authToken))
{
   // Use the new AuthenticationMiddleware
   app.UseMiddleware<TerraformRegistry.Middleware.AuthenticationMiddleware>(authToken);
}

var webFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "web");
if (Directory.Exists(webFolderPath))
{
   app.UseStaticFiles(new StaticFileOptions
   {
      FileProvider = new PhysicalFileProvider(webFolderPath),
      RequestPath = ""
   });
}

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

app.UseHttpsRedirection();

app.MapGet("/.well-known/terraform.json", ServiceDiscoveryHandlers.GetServiceDiscovery)
   .WithTags("Service Discovery")
   .WithDescription("Terraform service discovery endpoint")
   .Produces<ServiceDiscovery>();

app.MapGet("/v1/modules", (IModuleService moduleService, string? q, string? @namespace, string? provider, int offset, int limit) =>
    ModuleHandlers.ListModules(moduleService, q, @namespace, provider, offset, limit))
   .WithTags("Modules")
   .WithDescription("Lists or searches modules")
   .Produces<ModuleList>()
   .RequireAuthorization();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}", (string @namespace, string name, string provider, string version, IModuleService moduleService) =>
    ModuleHandlers.GetModule(@namespace, name, provider, version, moduleService))
   .WithTags("Modules")
   .WithDescription("Gets a specific module")
   .Produces<Module>()
   .ProducesProblem(404)
   .RequireAuthorization();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/versions", (string @namespace, string name, string provider, IModuleService moduleService) =>
    ModuleHandlers.GetModuleVersions(@namespace, name, provider, moduleService))
   .WithTags("Modules")
   .WithDescription("Gets all versions of a specific module")
   .Produces<ModuleVersions>()
   .RequireAuthorization();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}/download", (string @namespace, string name, string provider, string version, IModuleService moduleService) =>
    ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService))
   .WithTags("Modules")
   .WithDescription("Downloads a specific module version")
   .Produces(200, contentType: "application/zip")
   .ProducesProblem(404)
   .RequireAuthorization();

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name, string provider, string version, HttpRequest request, IModuleService moduleService) =>
    await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, moduleService))
   .WithTags("Modules")
   .WithDescription("Uploads a new module version")
   .Accepts<IFormFile>("multipart/form-data")
   .ProducesProblem(400)
   .ProducesProblem(409)
   .Produces(201)
   .RequireAuthorization();

app.MapFallback(async (HttpContext context) =>
{
   if (context.Request.Path.StartsWithSegments("/v1") ||
       context.Request.Path.StartsWithSegments("/.well-known") ||
       context.Request.Path.StartsWithSegments("/swagger"))
   {
      return;
   }

   var indexPath = Path.Combine(webFolderPath, "index.html");
   if (File.Exists(indexPath))
   {
      context.Response.ContentType = "text/html";
      await context.Response.SendFileAsync(indexPath);
   }
});

app.Run();

public partial class Program;