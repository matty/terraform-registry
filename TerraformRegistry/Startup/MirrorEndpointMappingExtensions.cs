using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace TerraformRegistry.Startup;

internal static class MirrorEndpointMappingExtensions
{
    public static WebApplication MapMirrorEndpoints(this WebApplication app)
    {
        app.MapGet("/mirror/providers/{hostname}/{namespace}/{type}/index.json", MirrorHandlers.GetProviderIndex)
            .WithTags("Mirror")
            .RequireRateLimiting(RateLimitPolicyNames.MirrorIngress)
            .WithDescription("Gets the Terraform provider network mirror index")
            .Produces<ProviderMirrorIndexResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/mirror/providers/{hostname}/{namespace}/{type}/{version}.json", MirrorHandlers.GetProviderVersion)
            .WithTags("Mirror")
            .RequireRateLimiting(RateLimitPolicyNames.MirrorIngress)
            .WithDescription("Gets Terraform provider network mirror version metadata")
            .Produces<ProviderMirrorVersionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/mirror/providers/{hostname}/{namespace}/{type}/{filename}", MirrorHandlers.GetProviderPackage)
            .WithTags("Mirror")
            .RequireRateLimiting(RateLimitPolicyNames.MirrorIngress)
            .WithDescription("Downloads a cached Terraform provider package using a signed mirror URL")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
