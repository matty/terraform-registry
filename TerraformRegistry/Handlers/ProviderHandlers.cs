using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

public static class ProviderHandlers
{
    public static async Task<IResult> ListProviderVersions(
        string @namespace,
        string type,
        IProviderService providerService)
    {
        var versions = await providerService.GetProviderVersionsAsync(@namespace, type);
        if (versions == null)
        {
            return Results.NotFound(new { errors = new[] { "Provider not found" } });
        }
        return Results.Ok(versions);
    }

    public static async Task<IResult> GetProviderPackage(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderService providerService)
    {
        var package = await providerService.GetProviderPackageAsync(@namespace, type, version, os, arch);
        if (package == null)
        {
            return Results.NotFound(new { errors = new[] { "Package not found" } });
        }
        return Results.Ok(package);
    }

    public static async Task<IResult> UploadProviderVersion(
        string @namespace,
        string type,
        string version,
        HttpRequest request,
        IProviderService providerService)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Content-Type must be multipart/form-data" });
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var os = form["os"].ToString();
        var arch = form["arch"].ToString();
        var shasum = form["shasum"].ToString();
        var signingKeyId = form["signing_key_id"].ToString();
        var protocols = form["protocols"].ToString();

        var protocolsList = new List<string> { "5.0" };
        if (!string.IsNullOrEmpty(protocols))
        {
            try { protocolsList = JsonSerializer.Deserialize<List<string>>(protocols) ?? protocolsList; }
            catch { /* Ignore invalid JSON, use default */ }
        }

        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "File is required" });
        if (string.IsNullOrEmpty(os)) return Results.BadRequest(new { error = "os is required" });
        if (string.IsNullOrEmpty(arch)) return Results.BadRequest(new { error = "arch is required" });
        if (string.IsNullOrEmpty(shasum)) return Results.BadRequest(new { error = "shasum is required" });
        if (string.IsNullOrEmpty(signingKeyId)) return Results.BadRequest(new { error = "signing_key_id is required" });

        using var stream = file.OpenReadStream();
        var result = await providerService.UploadProviderAsync(@namespace, type, version, os, arch, file.FileName, stream, shasum, signingKeyId, protocolsList);

        // Handle optional SHASUMS files
        var shasumsFile = form.Files.GetFile("shasums_file");
        if (shasumsFile != null && shasumsFile.Length > 0)
        {
            using var shaStream = shasumsFile.OpenReadStream();
            await providerService.UploadShasumsAsync(@namespace, type, version, shaStream);
        }

        var shasumsSigFile = form.Files.GetFile("shasums_sig_file");
        if (shasumsSigFile != null && shasumsSigFile.Length > 0)
        {
            using var sigStream = shasumsSigFile.OpenReadStream();
            await providerService.UploadShasumsSigAsync(@namespace, type, version, sigStream);
        }

        return Results.Created($"/v1/providers/{@namespace}/{type}/{version}/download/{os}/{arch}", result);
    }

    public static async Task<IResult> DownloadShasums(
        string @namespace,
        string type,
        string version,
        IProviderStorageService storageService)
    {
        var url = await storageService.GetShasumsDownloadUrlAsync(@namespace, type, version);
        if (string.IsNullOrEmpty(url)) return Results.NotFound();

        // If URL is external (Azure SAS), redirect
        if (url.StartsWith("http") && !url.Contains("/v1/providers/"))
            return Results.Redirect(url);

        // Otherwise, serve content stream (Local)
        var relativePath = Path.Combine("providers", @namespace, type, version, "SHA256SUMS");
        var stream = await storageService.GetFileStreamAsync(relativePath);

        if (stream != null)
             return Results.File(stream, "text/plain", "SHA256SUMS");

        // Fallback or loop handling
        return Results.NotFound();
    }

    public static async Task<IResult> DownloadShasumsSig(
        string @namespace,
        string type,
        string version,
        IProviderStorageService storageService)
    {
        var url = await storageService.GetShasumsSigDownloadUrlAsync(@namespace, type, version);
        if (string.IsNullOrEmpty(url)) return Results.NotFound();

        if (url.StartsWith("http") && !url.Contains("/v1/providers/"))
            return Results.Redirect(url);

        var relativePath = Path.Combine("providers", @namespace, type, version, "SHA256SUMS.sig");
        var stream = await storageService.GetFileStreamAsync(relativePath);

        if (stream != null)
             return Results.File(stream, "application/octet-stream", "SHA256SUMS.sig");

        return Results.NotFound();
    }

    public static async Task<IResult> DownloadProviderFile(
        string @namespace,
        string type,
        string version,
        string os,
        string arch,
        IProviderStorageService storageService,
        IConfiguration config)
    {
        // Reconstruct relative path logic used in Upload
        var fileName = $"{type}_{version}_{os}_{arch}.zip";
        var relativePath = Path.Combine("providers", @namespace, type, version, fileName);

        var stream = await storageService.GetFileStreamAsync(relativePath);
        if (stream != null)
            return Results.File(stream, "application/zip", fileName);

        return Results.NotFound();
    }
}
