using TerraformRegistry;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateSlimBuilder(args);

// Add services to the container
// Add configurations - with support for environment variables
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("TF_REG_") // Add environment variables with prefix
    .AddInMemoryCollection(new List<KeyValuePair<string, string?>>
    {
        new("BaseUrl", "http://localhost:5131"),
        new("ModuleStoragePath", Path.Combine(Directory.GetCurrentDirectory(), "modules")),
        new("DatabaseProvider", "inmemory"), // Add default database provider
        new("AuthorizationToken", null) // Add default for authorization token
    });

// Get the base URL from configuration
var baseUrl = builder.Configuration["BaseUrl"] ?? "http://localhost:5131";

// Register database service based on configuration
var databaseProvider = builder.Configuration["DatabaseProvider"]?.ToLower() ?? "inmemory";

switch (databaseProvider)
{
   case "postgres":
      var postgresConnectionString = builder.Configuration["PostgreSQL:ConnectionString"];
      if (string.IsNullOrEmpty(postgresConnectionString))
      {
         throw new Exception("PostgreSQL connection string is not configured");
      }

#if UsePostgreSQL
      builder.Services.AddSingleton<IDatabaseService>(provider =>
          new TerraformRegistry.PostgreSQL.PostgreSQLDatabaseService(postgresConnectionString, baseUrl));
      Console.WriteLine("Using PostgreSQL database for module metadata");
#else
      throw new Exception("PostgreSQL support is not included in this build. Rebuild with UsePostgreSQL=true");
#endif
      break;

   case "inmemory":
   default:
      builder.Services.AddSingleton<IDatabaseService>(provider =>
          new InMemoryDatabaseService(baseUrl));
      Console.WriteLine("Using in-memory database for module metadata");
      break;
}

// Choose which module service to use based on configuration
var storageProvider = builder.Configuration["StorageProvider"]?.ToLower() ?? "local";

switch (storageProvider)
{
   case "azure":
#if UseAzureBlob
      builder.Services.AddSingleton<IModuleService, TerraformRegistry.AzureBlob.AzureBlobModuleService>();
      Console.WriteLine("Using Azure Blob Storage for module storage");
#else
      throw new Exception("Azure Blob Storage support is not included in this build. Rebuild with UseAzureBlob=true");
#endif
      break;

   case "local":
   default:
      builder.Services.AddSingleton<IModuleService, LocalModuleService>();
      Console.WriteLine("Using local file system for module storage");
      break;
}

// Configure JSON serialization with source generation
builder.Services.ConfigureHttpJsonOptions(options =>
{
   options.SerializerOptions.TypeInfoResolver = AppJsonSerializerContext.Default;
});

// Add Swagger/OpenAPI support for development
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(options =>
{
   options.Title = "Terraform Registry API";
   options.Version = "v1";
   options.Description = "A private Terraform Registry API for modules";
});

var app = builder.Build();

// Authorization middleware for API endpoints
var authToken = builder.Configuration["AuthorizationToken"];
if (!string.IsNullOrEmpty(authToken))
{
   app.Use(async (context, next) =>
   {
      // Only protect API endpoints (not static files or root)
      var path = context.Request.Path.Value ?? string.Empty;
      if (path.StartsWith("/v1/") || path.StartsWith("/.well-known/"))
      {
         var header = context.Request.Headers["Authorization"].FirstOrDefault();
         if (string.IsNullOrEmpty(header) || !header.Equals($"Bearer {authToken}", StringComparison.Ordinal))
         {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: missing or invalid Authorization token.");
            return;
         }
      }
      await next();
   });
}

// Configure static files middleware for serving SPA
var webFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "web");
if (Directory.Exists(webFolderPath))
{
   app.UseStaticFiles(new StaticFileOptions
   {
      FileProvider = new PhysicalFileProvider(webFolderPath),
      RequestPath = ""
   });
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
   app.UseOpenApi();
   app.UseSwaggerUi();

   // No longer redirecting to Swagger docs from root
}

// In all environments, serve the SPA's index.html for the root route if it exists
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

// Define routes directly with minimal API patterns
// Service discovery endpoint
app.MapGet("/.well-known/terraform.json", ServiceDiscoveryHandlers.GetServiceDiscovery)
   .WithTags("Service Discovery")
   .WithDescription("Terraform service discovery endpoint")
   .Produces<ServiceDiscovery>();

// Module endpoints
app.MapGet("/v1/modules", (IModuleService moduleService, string? q, string? @namespace, string? provider, int offset, int limit) =>
    ModuleHandlers.ListModules(moduleService, q, @namespace, provider, offset, limit))
   .WithTags("Modules")
   .WithDescription("Lists or searches modules")
   .Produces<ModuleList>();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}", (string @namespace, string name, string provider, string version, IModuleService moduleService) =>
    ModuleHandlers.GetModule(@namespace, name, provider, version, moduleService))
   .WithTags("Modules")
   .WithDescription("Gets a specific module")
   .Produces<Module>()
   .ProducesProblem(404);

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/versions", (string @namespace, string name, string provider, IModuleService moduleService) =>
    ModuleHandlers.GetModuleVersions(@namespace, name, provider, moduleService))
   .WithTags("Modules")
   .WithDescription("Gets all versions of a specific module")
   .Produces<ModuleVersions>();

app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}/download", (string @namespace, string name, string provider, string version, IModuleService moduleService) =>
    ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService))
   .WithTags("Modules")
   .WithDescription("Downloads a specific module version")
   .Produces(200, contentType: "application/zip")
   .ProducesProblem(404);

app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name, string provider, string version, HttpRequest request, IModuleService moduleService) =>
    await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, moduleService))
   .WithTags("Modules")
   .WithDescription("Uploads a new module version")
   .Accepts<IFormFile>("multipart/form-data")
   .ProducesProblem(400)
   .ProducesProblem(409)
   .Produces(201);

// Add a fallback route for the SPA to handle client-side routing
app.MapFallback(async (HttpContext context) =>
{
   // Check if the request is for an API route - if so, let it 404 normally
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