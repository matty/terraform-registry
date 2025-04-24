namespace TerraformRegistry.Handlers;

using Models;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using static Microsoft.AspNetCore.Http.Results;

/// <summary>
/// Handlers for module operations
/// </summary>
public static class ModuleHandlers
{
    private static readonly ILogger _logger;

    // Static constructor to initialize the logger
    static ModuleHandlers()
    {
        // Create a logger factory and get a logger instance
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        _logger = loggerFactory.CreateLogger("ModuleHandlers");
    }

    /// <summary>
    /// Lists or searches modules
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
    /// Gets a specific module
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
        if (module == null)
        {
            return NotFound();
        }

        return Ok(module);
    }

    /// <summary>
    /// Gets all versions of a specific module
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
        return Ok(versions);
    }

    /// <summary>
    /// Downloads a specific module version
    /// </summary>
    public static async Task<IResult> DownloadModule(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService)
    {
        _logger.LogInformation("Downloading module: {Namespace}/{Name}/{Provider}/{Version}",
            @namespace, name, provider, version);

        var downloadPath = await moduleService.GetModuleDownloadPathAsync(@namespace, name, provider, version);
        if (downloadPath == null)
        {
            return NotFound();
        }

        // Check if the result is a URL (Azure Blob SAS URL)
        if (Uri.TryCreate(downloadPath, UriKind.Absolute, out var uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            _logger.LogInformation("Redirecting to blob storage URL for download");
            return Redirect(downloadPath);
        }

        // Otherwise, treat it as a local file path
        if (!System.IO.File.Exists(downloadPath))
        {
            _logger.LogWarning("Module file not found: {FilePath}", downloadPath);
            return NotFound("Module file not found");
        }

        return File(downloadPath, "application/zip", Path.GetFileName(downloadPath));
    }

    /// <summary>
    /// Uploads a new module version
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
            return BadRequest($"Version '{version}' is not a valid Semantic Version (SemVer 2.0.0). Expected format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]");
        }

        var form = await request.ReadFormAsync();
        var moduleFile = form.Files["moduleFile"];
        var description = form["description"].ToString() ?? string.Empty;

        if (moduleFile == null || moduleFile.Length == 0)
        {
            return BadRequest("No file uploaded");
        }

        try
        {
            using var stream = moduleFile.OpenReadStream();
            var result = await moduleService.UploadModuleAsync(@namespace, name, provider, version, stream, description);

            if (!result)
            {
                return Conflict("Module version already exists");
            }

            // Using location header instead of CreatedAtAction for minimal API
            return Created($"/v1/modules/{@namespace}/{name}/{provider}/{version}", null);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Version"))
        {
            _logger.LogWarning("Invalid version format: {Version}", version);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading module");
            return Problem("An error occurred while uploading the module", statusCode: 500);
        }
    }
}