using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Middleware;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

using static Results;

/// <summary>
///     Handlers for module operations
/// </summary>
public static class ModuleHandlers
{
    private static readonly ILogger _logger;

    // Static constructor to initialize the logger
    static ModuleHandlers()
    {
        // Create a logger factory and get a logger instance
        var loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });

        _logger = loggerFactory.CreateLogger("ModuleHandlers");
    }

    // Helper to return error responses in Terraform Registry format
    private static IResult Error(int statusCode, string message)
    {
        return ErrorResponseExtensions.TerraformError(statusCode, message);
    }

    private static IResult? CheckPermission(HttpContext context, string permission)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(permission))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);
        return null;
    }

    private static IResult? ValidateCoordinates(string @namespace, string name, string provider)
    {
        var error = ModuleIdentifierValidator.GetModuleCoordinateError(@namespace, name, provider);
        return error == null ? null : ErrorResponseExtensions.BadRequest(error);
    }

    /// <summary>
    ///     Lists or searches modules
    /// </summary>
    public static async Task<IResult> ListModules(
        IModuleService moduleService,
        HttpContext context,
        string? q = null,
        string? @namespace = null,
        string? provider = null,
        int offset = 0,
        int limit = 10)
    {
        var denied = CheckPermission(context, Permissions.ModulesRead);
        if (denied != null) return denied;

        _logger.LogInformation("Listing modules with query: {Query}, namespace: {Namespace}, provider: {Provider}",
            q, @namespace, provider);

        var request = new ModuleSearchRequest
        {
            Q = q,
            Namespace = @namespace,
            Provider = provider,
            Offset = offset,
            Limit = limit
        };

        var result = await moduleService.ListModulesAsync(request);
        return Ok(result);
    }

    /// <summary>
    ///     Gets a specific module
    /// </summary>
    public static async Task<IResult> GetModule(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesRead);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Getting module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        var module = await moduleService.GetModuleAsync(@namespace, name, provider, version);
        if (module == null) return ErrorResponseExtensions.NotFound("Module not found");

        return Ok(module);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public static async Task<IResult> GetModuleVersions(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesRead);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Getting versions for module: {Namespace}/{Name}/{Provider}",
            @namespace, name, provider);

        var versions = await moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        if (versions == null || versions.Modules == null || !versions.Modules.Any() ||
            versions.Modules.FirstOrDefault()?.Versions == null || !versions.Modules.FirstOrDefault()!.Versions.Any())
            return ErrorResponseExtensions.NotFound("Module not found");
        return Ok(versions);
    }

    /// <summary>
    ///     Downloads a specific module version
    /// </summary>
    public static async Task<IResult> DownloadModule(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        IDatabaseService dbService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesRead);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Downloading module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        var downloadPath = await moduleService.GetModuleDownloadPathAsync(@namespace, name, provider, version);
        if (downloadPath == null) return ErrorResponseExtensions.NotFound("Module not found");

        context.Response.Headers["X-Terraform-Get"] = downloadPath;

        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        _ = Task.Run(async () =>
        {
            try
            {
                await dbService.RecordDownloadAsync(@namespace, name, provider, version, clientIp, userAgent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record download for {Namespace}/{Name}/{Provider}/{Version}",
                    @namespace, name, provider, version);
            }
        });

        // Terraform CLI expects 204 + X-Terraform-Get. Portal/browser should follow a redirect.
        var accept = context.Request.Headers["Accept"].ToString();
        var isTerraformClient = userAgent.Contains("Terraform", StringComparison.OrdinalIgnoreCase) ||
                                accept.Contains("terraform", StringComparison.OrdinalIgnoreCase);

        if (isTerraformClient)
        {
            return NoContent();
        }

        context.Response.Headers.Location = downloadPath;
        context.Response.StatusCode = StatusCodes.Status302Found;
        return Empty;
    }

    /// <summary>
    ///     Downloads the latest version of a module for a provider
    /// </summary>
    public static async Task<IResult> DownloadLatestModule(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService,
        IDatabaseService dbService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesRead);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Downloading latest module: {Namespace}/{Name}/{Provider}",
            @namespace, name, provider);

        // Get all versions and pick the latest using SemVer sort
        var versions = await moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        var latestVersions = versions?.Modules?.FirstOrDefault()?.Versions;
        var latest = latestVersions?.OrderByDescending(v => v.Version, Comparer<string>.Create((a, b) =>
            SemVerValidator.Compare(a, b) ?? 0)).FirstOrDefault()?.Version;
        if (string.IsNullOrEmpty(latest)) return ErrorResponseExtensions.NotFound("Module not found");

        return await DownloadModule(@namespace, name, provider, latest, moduleService, dbService, context);
    }

    /// <summary>
    ///     Uploads a new module version
    /// </summary>
    public static async Task<IResult> UploadModule(
        string @namespace,
        string name,
        string provider,
        string version,
        HttpRequest request,
        IModuleService moduleService,
        WebhookDispatcher webhookDispatcher,
        IAuditService auditService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesUpload);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Uploading module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return ErrorResponseExtensions.BadRequest(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var form = await request.ReadFormAsync();
        var moduleFile = form.Files["moduleFile"];
        var description = form["description"].ToString() ?? string.Empty;

        if (moduleFile == null || moduleFile.Length == 0) return ErrorResponseExtensions.BadRequest("No file uploaded");

        try
        {
            // Parse optional replace parameter from form or query
            bool replace = false;
            var replaceRaw = form["replace"].ToString();
            if (string.IsNullOrWhiteSpace(replaceRaw) && request.Query.ContainsKey("replace"))
            {
                replaceRaw = request.Query["replace"].ToString();
            }

            if (!string.IsNullOrWhiteSpace(replaceRaw))
            {
                var val = replaceRaw.Trim();
                if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1")
                    replace = true;
                else if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase) || val == "0")
                    replace = false;
            }

            await using var stream = moduleFile.OpenReadStream();
            var result =
                await moduleService.UploadModuleAsync(@namespace, name, provider, version, stream, description,
                    replace);

            if (!result)
            {
                return ErrorResponseExtensions.Conflict("Module version already exists");
            }

            webhookDispatcher.FireEvent("module.published", @namespace, name, provider, version, description);
            context.FireAuditLog(auditService, "module.published", "module", $"{@namespace}/{name}/{provider}/{version}", new { @namespace, name, provider, version });

            // Return JSON with filename using DTO
            var response = new UploadModuleResponse { Filename = moduleFile.FileName };
            return Created($"/v1/modules/{@namespace}/{name}/{provider}/{version}", response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid module upload request for {Namespace}/{Name}/{Provider}/{Version}: {Message}",
                @namespace, name, provider, version, ex.Message);
            return ErrorResponseExtensions.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            _logger.LogInformation("Module version already exists: {Namespace}/{Name}/{Provider}/{Version}",
                @namespace, name, provider, version);
            return ErrorResponseExtensions.Conflict(ex.Message);
        }
        // Let other exceptions bubble up to the global exception handler
    }

    /// <summary>
    ///     Soft deletes a module version
    /// </summary>
    public static async Task<IResult> DeleteModuleVersion(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        WebhookDispatcher webhookDispatcher,
        IAuditService auditService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesDelete);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Deleting module version: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return ErrorResponseExtensions.BadRequest(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var result = await moduleService.DeleteModuleVersionAsync(@namespace, name, provider, version);
        if (!result) return ErrorResponseExtensions.NotFound("Module version not found");

        webhookDispatcher.FireEvent("module.deleted", @namespace, name, provider, version, null);
        context.FireAuditLog(auditService, "module.deleted", "module", $"{@namespace}/{name}/{provider}/{version}", new { @namespace, name, provider, version });

        return NoContent();
    }

    /// <summary>
    ///     Restores a soft-deleted module version
    /// </summary>
    public static async Task<IResult> RestoreModuleVersion(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        WebhookDispatcher webhookDispatcher,
        IAuditService auditService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesRestore);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Restoring module version: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return ErrorResponseExtensions.BadRequest(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var result = await moduleService.RestoreModuleVersionAsync(@namespace, name, provider, version);
        if (!result) return ErrorResponseExtensions.NotFound("Deleted module version not found");

        webhookDispatcher.FireEvent("module.restored", @namespace, name, provider, version, null);
        context.FireAuditLog(auditService, "module.restored", "module", $"{@namespace}/{name}/{provider}/{version}", new { @namespace, name, provider, version });

        return NoContent();
    }

    /// <summary>
    ///     Permanently deletes a module version (purge)
    /// </summary>
    public static async Task<IResult> PurgeModuleVersion(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        WebhookDispatcher webhookDispatcher,
        IAuditService auditService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesPurge);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Purging module version: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return ErrorResponseExtensions.BadRequest(
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var result = await moduleService.PurgeModuleVersionAsync(@namespace, name, provider, version);
        if (!result) return ErrorResponseExtensions.NotFound("Deleted module version not found");

        webhookDispatcher.FireEvent("module.purged", @namespace, name, provider, version, null);
        context.FireAuditLog(auditService, "module.purged", "module", $"{@namespace}/{name}/{provider}/{version}", new { @namespace, name, provider, version });

        return NoContent();
    }

    /// <summary>
    ///     Lists all soft-deleted modules
    /// </summary>
    public static async Task<IResult> ListDeletedModules(
        IModuleService moduleService,
        HttpContext context,
        string? q = null,
        string? @namespace = null,
        string? provider = null,
        int offset = 0,
        int limit = 10)
    {
        var denied = CheckPermission(context, Permissions.ModulesDelete);
        if (denied != null) return denied;

        _logger.LogInformation(
            "Listing deleted modules with query: {Query}, namespace: {Namespace}, provider: {Provider}",
            q, @namespace, provider);

        var request = new ModuleSearchRequest
        {
            Q = q,
            Namespace = @namespace,
            Provider = provider,
            Offset = offset,
            Limit = limit
        };

        var result = await moduleService.ListDeletedModulesAsync(request);
        return Ok(result);
    }

    /// <summary>
    ///     Updates the description for a module
    /// </summary>
    public static async Task<IResult> UpdateDescription(
        string @namespace, string name, string provider,
        HttpRequest request, IModuleService moduleService,
        IAuditService auditService,
        HttpContext context)
    {
        var denied = CheckPermission(context, Permissions.ModulesDescription);
        if (denied != null) return denied;
        var invalid = ValidateCoordinates(@namespace, name, provider);
        if (invalid != null) return invalid;

        _logger.LogInformation("Updating description for module {Namespace}/{Name}/{Provider}",
            @namespace, name, provider);

        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var description = json.RootElement.GetProperty("description").GetString() ?? string.Empty;

            var result = await moduleService.UpdateModuleDescriptionAsync(@namespace, name, provider, description);
            if (!result) return ErrorResponseExtensions.NotFound("Module not found");

            context.FireAuditLog(auditService, "module.description_updated", "module", $"{@namespace}/{name}/{provider}", new { @namespace, name, provider, description });

            return Ok(new { description });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating description for module {Namespace}/{Name}/{Provider}",
                @namespace, name, provider);
            return Error(400, "Invalid request body");
        }
    }
}
