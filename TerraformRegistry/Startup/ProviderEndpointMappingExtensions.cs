using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Startup;

internal static class ProviderEndpointMappingExtensions
{
    public static WebApplication MapProviderEndpoints(this WebApplication app)
    {
        app.MapProviderManagementEndpoints();
        app.MapProviderRegistryEndpoints();
        app.MapProviderDownloadEndpoint();

        return app;
    }

    private static WebApplication MapProviderManagementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/providers",
                (IProviderRegistryService service, HttpContext context, string? q, int offset = 0, int limit = 20) =>
                    ProviderHandlers.ListProviders(service, context, q, offset, limit))
            .WithTags("Providers");

        app.MapPost("/api/providers", (IProviderRegistryService service, HttpContext context, HttpRequest request) =>
                ProviderHandlers.CreateProvider(service, context, request))
            .WithTags("Providers");

        app.MapGet("/api/providers/{namespace}/{type}",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
                    ProviderHandlers.GetProvider(@namespace, type, service, context))
            .WithTags("Providers");

        app.MapPatch("/api/providers/{namespace}/{type}",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context,
                        HttpRequest request) =>
                    ProviderHandlers.UpdateProvider(@namespace, type, service, context, request))
            .WithTags("Providers");

        app.MapDelete("/api/providers/{namespace}/{type}",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
                    ProviderHandlers.DeleteProvider(@namespace, type, service, context))
            .WithTags("Providers");

        app.MapGet("/api/providers/{namespace}/{type}/gpg-keys",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
                    ProviderHandlers.ListGpgKeys(@namespace, type, service, context))
            .WithTags("Providers");

        app.MapPost("/api/providers/{namespace}/{type}/gpg-keys",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context,
                        HttpRequest request) =>
                    ProviderHandlers.AddGpgKey(@namespace, type, service, context, request))
            .WithTags("Providers");

        app.MapDelete("/api/providers/{namespace}/{type}/gpg-keys/{keyId}",
                (string @namespace, string type, string keyId, IProviderRegistryService service,
                        HttpContext context) =>
                    ProviderHandlers.RevokeGpgKey(@namespace, type, keyId, service, context))
            .WithTags("Providers");

        app.MapGet("/api/providers/{namespace}/{type}/versions",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context) =>
                    ProviderHandlers.ListVersions(@namespace, type, service, context))
            .WithTags("Providers");

        app.MapPost("/api/providers/{namespace}/{type}/versions",
                (string @namespace, string type, IProviderRegistryService service, HttpContext context,
                        HttpRequest request) =>
                    ProviderHandlers.CreateVersion(@namespace, type, service, context, request))
            .WithTags("Providers");

        app.MapDelete("/api/providers/{namespace}/{type}/versions/{version}",
                (string @namespace, string type, string version, IProviderRegistryService service,
                        HttpContext context) =>
                    ProviderHandlers.DeleteVersion(@namespace, type, version, service, context))
            .WithTags("Providers");

        app.MapGet("/api/providers/{namespace}/{type}/versions/{version}/platforms",
                (string @namespace, string type, string version, IProviderRegistryService service,
                        HttpContext context) =>
                    ProviderHandlers.ListPlatforms(@namespace, type, version, service, context))
            .WithTags("Providers");

        app.MapPost("/api/providers/{namespace}/{type}/versions/{version}/platforms",
                (string @namespace, string type, string version, IProviderRegistryService service,
                        HttpContext context, HttpRequest request) =>
                    ProviderHandlers.CreatePlatform(@namespace, type, version, service, context, request))
            .WithTags("Providers");

        app.MapDelete("/api/providers/{namespace}/{type}/versions/{version}/platforms/{os}/{arch}",
                (string @namespace, string type, string version, string os, string arch,
                        IProviderRegistryService service, HttpContext context) =>
                    ProviderHandlers.DeletePlatform(@namespace, type, version, os, arch, service, context))
            .WithTags("Providers");

        app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/shasums",
                (string @namespace, string type, string version, IProviderRegistryService service,
                        HttpContext context, HttpRequest request) =>
                    ProviderHandlers.UploadShasums(@namespace, type, version, service, context, request))
            .WithTags("Providers");

        app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/shasums.sig",
                (string @namespace, string type, string version, IProviderRegistryService service,
                        HttpContext context, HttpRequest request) =>
                    ProviderHandlers.UploadShasumsSignature(@namespace, type, version, service, context, request))
            .WithTags("Providers");

        app.MapPut("/api/providers/{namespace}/{type}/versions/{version}/platforms/{os}/{arch}/package",
                (string @namespace, string type, string version, string os, string arch,
                        IProviderRegistryService service, HttpContext context, HttpRequest request) =>
                    ProviderHandlers.UploadPlatformPackage(@namespace, type, version, os, arch, service, context,
                        request))
            .WithTags("Providers");

        return app;
    }

    private static WebApplication MapProviderRegistryEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/providers/{namespace}/{type}/versions",
                (string @namespace, string type, IProviderRegistryService providerService, HttpContext context) =>
                    ProviderHandlers.GetVersions(@namespace, type, providerService, context))
            .WithTags("Providers")
            .WithDescription("Gets all versions for a provider")
            .Produces<ProviderVersionsResponse>()
            .ProducesProblem(404);

        app.MapGet("/v1/providers/{namespace}/{type}/{version}/download/{os}/{arch}",
                (string @namespace, string type, string version, string os, string arch,
                        IProviderRegistryService providerService, HttpContext context) =>
                    ProviderHandlers.GetPackage(@namespace, type, version, os, arch, providerService, context))
            .WithTags("Providers")
            .WithDescription("Gets package metadata for a provider version and platform")
            .Produces<ProviderPackageResponse>()
            .ProducesProblem(404);

        return app;
    }

    private static WebApplication MapProviderDownloadEndpoint(this WebApplication app)
    {
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

        return app;
    }
}
