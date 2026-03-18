using System.Security.Claims;
using System.Security.Cryptography;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

public static class VcsHandlers
{
    public static async Task<IResult> ListVcsSources(IVcsSourceService vcsService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var sources = await vcsService.ListVcsSourcesAsync(userId);
        return Results.Ok(sources);
    }

    public static async Task<IResult> CreateVcsSource(IVcsSourceService vcsService, IConfiguration configuration, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var body = await request.ReadFromJsonAsync<CreateVcsSourceRequest>();
        if (body == null
            || string.IsNullOrWhiteSpace(body.Namespace)
            || string.IsNullOrWhiteSpace(body.Name)
            || string.IsNullOrWhiteSpace(body.Provider)
            || string.IsNullOrWhiteSpace(body.RepoOwner)
            || string.IsNullOrWhiteSpace(body.RepoName))
        {
            return Results.BadRequest(new { error = "namespace, name, provider, repoOwner, and repoName are required" });
        }

        string? patEncrypted = null;
        if (!string.IsNullOrEmpty(body.Pat))
        {
            var encryptionKey = configuration["EncryptionKey"];
            if (string.IsNullOrEmpty(encryptionKey))
            {
                return Results.BadRequest(new { error = "EncryptionKey is not configured; cannot store PAT" });
            }

            patEncrypted = EncryptionHelper.Encrypt(body.Pat, encryptionKey);
        }

        var webhookSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var source = await vcsService.CreateVcsSourceAsync(
            userId, body.Namespace, body.Name, body.Provider,
            body.RepoOwner, body.RepoName, patEncrypted, webhookSecret);

        var baseUrl = configuration["BaseUrl"] ?? "http://localhost:5131";
        var webhookUrl = $"{baseUrl.TrimEnd('/')}/api/vcs/github/webhook";

        context.FireAuditLog(auditService, "vcs.created", "vcs_source", source.Id.ToString(), new { @namespace = body.Namespace, name = body.Name, provider = body.Provider, repoOwner = body.RepoOwner, repoName = body.RepoName });

        return Results.Created($"/api/vcs/sources/{source.Id}", new
        {
            source.Id,
            source.Namespace,
            source.Name,
            source.Provider,
            source.RepoOwner,
            source.RepoName,
            source.IsActive,
            source.CreatedAt,
            source.UpdatedAt,
            webhookSecret,
            webhookUrl
        });
    }

    public static async Task<IResult> UpdateVcsSource(Guid id, IVcsSourceService vcsService, IConfiguration configuration, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var body = await request.ReadFromJsonAsync<UpdateVcsSourceRequest>();

        string? patEncrypted = null;
        if (!string.IsNullOrEmpty(body?.Pat))
        {
            var encryptionKey = configuration["EncryptionKey"];
            if (string.IsNullOrEmpty(encryptionKey))
            {
                return Results.BadRequest(new { error = "EncryptionKey is not configured; cannot store PAT" });
            }

            patEncrypted = EncryptionHelper.Encrypt(body.Pat, encryptionKey);
        }

        var updated = await vcsService.UpdateVcsSourceAsync(id, userId, body?.RepoOwner, body?.RepoName, patEncrypted, body?.IsActive);
        if (updated == null) return Results.NotFound(new { error = "VCS source not found or access denied" });

        context.FireAuditLog(auditService, "vcs.updated", "vcs_source", id.ToString());

        return Results.Ok(updated);
    }

    public static async Task<IResult> DeleteVcsSource(Guid id, IVcsSourceService vcsService, IAuditService auditService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var result = await vcsService.DeleteVcsSourceAsync(id, userId);
        if (!result) return Results.NotFound(new { error = "VCS source not found or access denied" });

        context.FireAuditLog(auditService, "vcs.deleted", "vcs_source", id.ToString());

        return Results.NoContent();
    }

    public static async Task<IResult> HandleGitHubWebhook(GitHubVcsService githubService, HttpContext context)
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        var signature = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var eventType = context.Request.Headers["X-GitHub-Event"].FirstOrDefault();

        var (status, reason, version) = await githubService.HandleWebhookAsync(signature, eventType, body);
        return Results.Ok(new { status, reason, version });
    }
}

public record CreateVcsSourceRequest(string Namespace, string Name, string Provider, string RepoOwner, string RepoName, string? Pat);
public record UpdateVcsSourceRequest(string? RepoOwner, string? RepoName, string? Pat, bool? IsActive);
