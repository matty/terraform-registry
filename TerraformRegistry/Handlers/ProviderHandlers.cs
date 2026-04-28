using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Middleware;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class ProviderHandlers
{
    private static IResult? CheckPermission(HttpContext context, string permission)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(permission))
            return ErrorResponseExtensions.TerraformError(403, "Insufficient permissions");
        return null;
    }

    private static IResult? ValidateCoordinates(string @namespace, string type)
    {
        var error = ProviderIdentifierValidator.GetProviderCoordinateError(@namespace, type);
        return error == null ? null : ErrorResponseExtensions.BadRequest(error);
    }

    public static async Task<IResult> GetVersions(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var versions = await providerService.GetVersionsAsync(@namespace, type);
        return versions == null
            ? ErrorResponseExtensions.NotFound("Provider not found")
            : Results.Ok(versions);
    }

    public static async Task<IResult> GetPackage(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var package = await providerService.GetPackageAsync(
            @namespace,
            type,
            version,
            os,
            arch,
            clientIp,
            userAgent,
            context.RequestAborted);

        return package == null
            ? ErrorResponseExtensions.NotFound("Provider package not found")
            : Results.Ok(package);
    }
}
