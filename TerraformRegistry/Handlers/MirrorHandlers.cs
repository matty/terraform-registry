using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class MirrorHandlers
{
    private static IResult? CheckMirrorRead(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.MirrorRead)
            ? Results.Problem("Insufficient permissions", statusCode: StatusCodes.Status403Forbidden)
            : null;
    }

    public static async Task<IResult> GetProviderIndex(
        string hostname,
        string @namespace,
        string type,
        IProviderMirrorService mirrorService,
        HttpContext context)
    {
        var denied = CheckMirrorRead(context);
        if (denied is not null) return denied;

        var index = await mirrorService.GetProviderIndexAsync(
            hostname,
            @namespace,
            type,
            context.RequestAborted);

        return index is null
            ? Results.Problem("Provider mirror index was not found.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(index);
    }

    public static async Task<IResult> GetProviderVersion(
        string hostname,
        string @namespace,
        string type,
        string version,
        IProviderMirrorService mirrorService,
        HttpContext context)
    {
        var denied = CheckMirrorRead(context);
        if (denied is not null) return denied;

        var metadata = await mirrorService.GetProviderVersionAsync(
            hostname,
            @namespace,
            type,
            version,
            context.RequestAborted);

        return metadata is null
            ? Results.Problem("Provider mirror version metadata was not found.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(metadata);
    }

    public static async Task<IResult> GetProviderPackage(
        string hostname,
        string @namespace,
        string type,
        string filename,
        IProviderMirrorService mirrorService,
        HttpContext context)
    {
        var download = await mirrorService.OpenPackageAsync(
            hostname,
            @namespace,
            type,
            filename,
            context.Request.Query.ToDictionary(
                x => x.Key,
                x => x.Value.Where(value => value is not null).Select(value => value!).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            context.RequestAborted);

        if (download is null)
        {
            return Results.Problem("Provider mirror package was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return Results.File(
            download.Content,
            download.ContentType,
            download.Filename,
            lastModified: null,
            entityTag: null,
            enableRangeProcessing: false);
    }
}
