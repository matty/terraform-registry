using System.Security.Claims;
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

    private static IResult? ValidateVersionAndPlatform(string @namespace, string type, string? version = null, string? os = null, string? arch = null)
    {
        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        if (!string.IsNullOrWhiteSpace(version) && !SemVerValidator.IsValid(version))
        {
            return ErrorResponseExtensions.BadRequest(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        if (os != null && !ProviderIdentifierValidator.IsValidProviderSegment(os))
            return ErrorResponseExtensions.BadRequest("Invalid provider platform os. Use lowercase letters, numbers, or hyphens; start with a letter or number.");
        if (arch != null && !ProviderIdentifierValidator.IsValidProviderSegment(arch))
            return ErrorResponseExtensions.BadRequest("Invalid provider platform architecture. Use lowercase letters, numbers, or hyphens; start with a letter or number.");

        return null;
    }

    private static IResult? HandleInvalidOperation(InvalidOperationException ex)
    {
        if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            return ErrorResponseExtensions.Conflict(ex.Message);

        if (ex.Message.Contains("active provider versions", StringComparison.OrdinalIgnoreCase))
            return ErrorResponseExtensions.Conflict(ex.Message);

        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return ErrorResponseExtensions.NotFound(ex.Message);

        if (ex.Message.Contains("exceeds the configured limit", StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        return null;
    }

    private static async Task<IResult?> EnsureProviderExists(
        string @namespace,
        string type,
        IProviderRegistryService providerService)
    {
        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var provider = await providerService.GetProviderAsync(@namespace, type);
        return provider == null ? ErrorResponseExtensions.NotFound("Provider not found") : null;
    }

    private static async Task<IResult?> EnsurePlatformExists(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderRegistryService providerService)
    {
        var invalid = ValidateVersionAndPlatform(@namespace, type, version, os, arch);
        if (invalid != null) return invalid;

        var platforms = await providerService.GetManagementPlatformsAsync(@namespace, type, version);
        if (platforms == null) return ErrorResponseExtensions.NotFound("Provider version not found");

        var platform = platforms.Platforms.FirstOrDefault(p =>
            string.Equals(p.Os, os, StringComparison.Ordinal) &&
            string.Equals(p.Arch, arch, StringComparison.Ordinal));
        return platform == null ? ErrorResponseExtensions.NotFound("Provider platform not found") : null;
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

    public static async Task<IResult> ListProviders(
        IProviderRegistryService providerService,
        HttpContext context,
        string? q,
        int offset,
        int limit)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var providers = await providerService.ListProvidersAsync(q, offset, limit);
        var total = await providerService.CountProvidersAsync(q);
        return Results.Ok(new { providers, offset = Math.Max(0, offset), limit = Math.Clamp(limit, 1, 100), total });
    }

    public static async Task<IResult> CreateProvider(
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        var body = await request.ReadFromJsonAsync<CreateProviderRequest>();
        if (body == null) return ErrorResponseExtensions.BadRequest("Request body is required.");

        try
        {
            var actorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var provider = await providerService.CreateProviderAsync(body, actorUserId);
            return Results.Created($"/api/providers/{provider.Namespace}/{provider.Type}", provider);
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> GetProvider(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var provider = await providerService.GetProviderAsync(@namespace, type);
        return provider == null ? ErrorResponseExtensions.NotFound("Provider not found") : Results.Ok(provider);
    }

    public static async Task<IResult> UpdateProvider(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersDescription);
        if (denied != null) return denied;

        var body = await request.ReadFromJsonAsync<UpdateProviderRequest>();
        if (body == null) return ErrorResponseExtensions.BadRequest("Request body is required.");

        try
        {
            var updated = await providerService.UpdateProviderAsync(@namespace, type, body);
            return updated
                ? Results.Ok(await providerService.GetProviderAsync(@namespace, type))
                : ErrorResponseExtensions.NotFound("Provider not found");
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
    }

    public static async Task<IResult> DeleteProvider(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersDelete);
        if (denied != null) return denied;

        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var deleted = await providerService.DeleteProviderAsync(@namespace, type);
        return deleted ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider not found");
    }

    public static async Task<IResult> ListGpgKeys(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var missing = await EnsureProviderExists(@namespace, type, providerService);
        if (missing != null) return missing;

        var keys = await providerService.ListGpgKeysAsync(@namespace);
        return Results.Ok(new { gpg_keys = keys });
    }

    public static async Task<IResult> AddGpgKey(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersKeysManage);
        if (denied != null) return denied;

        var missing = await EnsureProviderExists(@namespace, type, providerService);
        if (missing != null) return missing;

        var body = await request.ReadFromJsonAsync<CreateProviderGpgKeyRequest>();
        if (body == null) return ErrorResponseExtensions.BadRequest("Request body is required.");

        try
        {
            var key = await providerService.AddGpgKeyAsync(@namespace, body);
            return Results.Created($"/api/providers/{@namespace}/{type}/gpg-keys/{key.KeyId}", key);
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> RevokeGpgKey(
        string @namespace,
        string type,
        string keyId,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersKeysManage);
        if (denied != null) return denied;

        var missing = await EnsureProviderExists(@namespace, type, providerService);
        if (missing != null) return missing;

        try
        {
            var revoked = await providerService.RevokeGpgKeyAsync(@namespace, keyId);
            return revoked ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider GPG key not found");
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> ListVersions(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var invalid = ValidateCoordinates(@namespace, type);
        if (invalid != null) return invalid;

        var versions = await providerService.GetManagementVersionsAsync(@namespace, type);
        return versions == null ? ErrorResponseExtensions.NotFound("Provider not found") : Results.Ok(versions);
    }

    public static async Task<IResult> CreateVersion(
        string @namespace,
        string type,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        var body = await request.ReadFromJsonAsync<CreateProviderVersionRequest>();
        if (body == null) return ErrorResponseExtensions.BadRequest("Request body is required.");

        try
        {
            var version = await providerService.CreateVersionAsync(@namespace, type, body);
            return Results.Created($"/api/providers/{@namespace}/{type}/versions/{version.Version}", version);
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> UploadShasums(
        string @namespace,
        string type,
        string version,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        if (request.ContentLength == 0) return ErrorResponseExtensions.BadRequest("SHA256SUMS content is required.");

        try
        {
            var uploaded = await providerService.UploadShasumsAsync(@namespace, type, version, request.Body, context.RequestAborted);
            return uploaded ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider version not found");
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> UploadShasumsSignature(
        string @namespace,
        string type,
        string version,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        if (request.ContentLength == 0) return ErrorResponseExtensions.BadRequest("SHA256SUMS signature content is required.");

        try
        {
            var uploaded = await providerService.UploadShasumsSignatureAsync(@namespace, type, version, request.Body, context.RequestAborted);
            return uploaded ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider version not found");
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> DeleteVersion(
        string @namespace,
        string type,
        string version,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersDelete);
        if (denied != null) return denied;

        var invalid = ValidateVersionAndPlatform(@namespace, type, version);
        if (invalid != null) return invalid;

        var deleted = await providerService.DeleteVersionAsync(@namespace, type, version);
        return deleted ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider version not found");
    }

    public static async Task<IResult> ListPlatforms(
        string @namespace,
        string type,
        string version,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersRead);
        if (denied != null) return denied;

        var invalid = ValidateVersionAndPlatform(@namespace, type, version);
        if (invalid != null) return invalid;

        var platforms = await providerService.GetManagementPlatformsAsync(@namespace, type, version);
        return platforms == null
            ? ErrorResponseExtensions.NotFound("Provider version not found")
            : Results.Ok(platforms);
    }

    public static async Task<IResult> CreatePlatform(
        string @namespace,
        string type,
        string version,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        var body = await request.ReadFromJsonAsync<CreateProviderPlatformRequest>();
        if (body == null) return ErrorResponseExtensions.BadRequest("Request body is required.");

        try
        {
            var platform = await providerService.CreatePlatformAsync(@namespace, type, version, body);
            return Results.Created($"/api/providers/{@namespace}/{type}/versions/{version}/platforms/{platform.Os}/{platform.Arch}", platform);
        }
        catch (ArgumentException ex)
        {
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (HandleInvalidOperation(ex) is { } result)
        {
            return result;
        }
    }

    public static async Task<IResult> UploadPlatformPackage(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderRegistryService providerService,
        HttpContext context,
        HttpRequest request)
    {
        var denied = CheckPermission(context, Permissions.ProvidersPublish);
        if (denied != null) return denied;

        var missing = await EnsurePlatformExists(@namespace, type, version, os, arch, providerService);
        if (missing != null) return missing;

        if (request.ContentLength == 0) return ErrorResponseExtensions.BadRequest("Provider package content is required.");

        var uploaded = await providerService.UploadPlatformPackageAsync(@namespace, type, version, os, arch, request.Body, context.RequestAborted);
        return uploaded ? Results.NoContent() : ErrorResponseExtensions.BadRequest("Provider package validation failed.");
    }

    public static async Task<IResult> DeletePlatform(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderRegistryService providerService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ProvidersDelete);
        if (denied != null) return denied;

        var invalid = ValidateVersionAndPlatform(@namespace, type, version, os, arch);
        if (invalid != null) return invalid;

        var deleted = await providerService.DeletePlatformAsync(@namespace, type, version, os, arch);
        return deleted ? Results.NoContent() : ErrorResponseExtensions.NotFound("Provider platform not found");
    }
}
