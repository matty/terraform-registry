using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using TerraformRegistry.Models;

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
        return Json(new { errors = new[] { message } }, statusCode: statusCode);
    }

    /// <summary>
    ///     Lists or searches modules
    /// </summary>
    public static async Task<IResult> ListModules(
        IModuleService moduleService,
        string? q = null,
        string? @namespace = null,
        string? provider = null,
        int offset = 0,
        int limit = 10)
    {
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
        IModuleService moduleService)
    {
        _logger.LogInformation("Getting module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        var module = await moduleService.GetModuleAsync(@namespace, name, provider, version);
        if (module == null) return Error(404, "Module not found");

        return Ok(module);
    }

    /// <summary>
    ///     Gets all versions of a specific module
    /// </summary>
    public static async Task<IResult> GetModuleVersions(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService)
    {
        _logger.LogInformation("Getting versions for module: {Namespace}/{Name}/{Provider}",
            @namespace, name, provider);

        var versions = await moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        if (versions == null || versions.Modules == null || !versions.Modules.Any() ||
            versions.Modules.FirstOrDefault()?.Versions == null || !versions.Modules.FirstOrDefault()!.Versions.Any())
            return Error(404, "Module not found");
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
        HttpContext context)
    {
        _logger.LogInformation("Downloading module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        var downloadPath = await moduleService.GetModuleDownloadPathAsync(@namespace, name, provider, version);
        if (downloadPath == null) return Error(404, "Module not found");

        context.Response.Headers["X-Terraform-Get"] = downloadPath;
        return NoContent();
    }

    /// <summary>
    ///     Downloads the latest version of a module for a provider
    /// </summary>
    public static async Task<IResult> DownloadLatestModule(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService,
        HttpContext context)
    {
        _logger.LogInformation("Downloading latest module: {Namespace}/{Name}/{Provider}",
            @namespace, name, provider);

        // Get all versions and pick the latest using SemVer sort
        var versions = await moduleService.GetModuleVersionsAsync(@namespace, name, provider);
        var latestVersions = versions?.Modules?.FirstOrDefault()?.Versions;
        var latest = latestVersions?.OrderByDescending(v => v.Version, Comparer<string>.Create((a, b) =>
            SemVerValidator.Compare(a, b) ?? 0)).FirstOrDefault()?.Version;
        if (string.IsNullOrEmpty(latest)) return Error(404, "Module not found");

        return await DownloadModule(@namespace, name, provider, latest, moduleService, context);
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
        IModuleService moduleService)
    {
        _logger.LogInformation("Uploading module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        // Validate the version string against SemVer 2.0.0 specification
        if (!SemVerValidator.IsValid(version))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return Error(400,
                $"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var form = await request.ReadFormAsync();
        var moduleFile = form.Files["moduleFile"];
        var description = form["description"].ToString() ?? string.Empty;

        if (moduleFile == null || moduleFile.Length == 0) return Error(400, "No file uploaded");

        try
        {
            await using var stream = moduleFile.OpenReadStream();
            var result =
                await moduleService.UploadModuleAsync(@namespace, name, provider, version, stream, description);

            if (!result) return Error(409, "Module version already exists");

            // Return JSON with filename using DTO
            var response = new UploadModuleResponse { Filename = moduleFile.FileName };
            return Created($"/v1/modules/{@namespace}/{name}/{provider}/{version}", response);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Version"))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading module");
            return Error(500, "An error occurred while uploading the module");
        }
    }
}