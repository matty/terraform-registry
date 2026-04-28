using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
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

    public static async Task<IResult> CreateVcsSource(IVcsSourceService vcsService, IVcsConnectionService connectionService, IGitHubVcsService githubService, IAuditService auditService, HttpContext context, HttpRequest request)
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
            || string.IsNullOrWhiteSpace(body.RepoName)
            || body.ConnectionId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "namespace, name, provider, repoOwner, repoName, and connectionId are required" });
        }

        // Verify the connection exists and is active before linking a source to it.
        var conn = await connectionService.GetConnectionAsync(body.ConnectionId);
        if (conn == null)
        {
            return Results.BadRequest(new { error = "VCS connection not found" });
        }
        if (!conn.IsActive)
        {
            return Results.BadRequest(new { error = "VCS connection is inactive" });
        }

        var source = await vcsService.CreateVcsSourceAsync(
            userId, body.Namespace, body.Name, body.Provider,
            body.RepoOwner, body.RepoName, body.ConnectionId);

        SyncVcsSourceResult? sync = null;
        if (body.SyncExistingTags)
        {
            try
            {
                sync = await githubService.SyncSourceAsync(source.Id, null, false, userId, context.RequestAborted);
            }
            catch (Exception ex)
            {
                sync = new SyncVcsSourceResult("failed", 0, 0, null, ex.Message);
            }
        }

        source = await vcsService.GetAsync(source.Id) ?? source;

        context.FireAuditLog(auditService, "vcs.created", "vcs_source", source.Id.ToString(), new { @namespace = body.Namespace, name = body.Name, provider = body.Provider, repoOwner = body.RepoOwner, repoName = body.RepoName, connectionId = body.ConnectionId, body.SyncExistingTags });

        return Results.Created($"/api/vcs/sources/{source.Id}", new
        {
            source.Id,
            source.Namespace,
            source.Name,
            source.Provider,
            source.RepoOwner,
            source.RepoName,
            source.ConnectionId,
            source.IsActive,
            source.TagPattern,
            source.LastPublishedVersion,
            source.LastSyncStatus,
            source.LastSyncAt,
            source.LastSyncError,
            source.CreatedAt,
            source.UpdatedAt,
            sync
        });
    }

    public static async Task<IResult> UpdateVcsSource(Guid id, IVcsSourceService vcsService, IVcsConnectionService connectionService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var body = await request.ReadFromJsonAsync<UpdateVcsSourceRequest>();

        if (body?.ConnectionId is Guid connectionId)
        {
            var conn = await connectionService.GetConnectionAsync(connectionId);
            if (conn == null)
            {
                return Results.BadRequest(new { error = "VCS connection not found" });
            }
            if (!conn.IsActive)
            {
                return Results.BadRequest(new { error = "VCS connection is inactive" });
            }
        }

        var updated = await vcsService.UpdateVcsSourceAsync(id, userId, body?.RepoOwner, body?.RepoName, body?.ConnectionId, body?.IsActive);
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

    public static async Task<IResult> GetVcsSourceByModule(IVcsSourceService vcsService, HttpContext context, string @namespace, string name, string provider)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        if (!context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var source = await vcsService.GetByModuleAsync(@namespace, name, provider);
        return source == null
            || !string.Equals(source.UserId, userId, StringComparison.Ordinal)
            ? Results.NotFound(new { error = "VCS source not found" })
            : Results.Ok(source);
    }

    public static async Task<IResult> SyncVcsSource(Guid id, IVcsSourceService vcsService, IGitHubVcsService githubService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        if (!context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var body = await request.ReadFromJsonAsync<SyncVcsSourceRequest>() ?? new SyncVcsSourceRequest(null, false);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var source = await vcsService.GetAsync(id);
        if (source == null || !string.Equals(source.UserId, userId, StringComparison.Ordinal))
        {
            return Results.NotFound(new { error = "VCS source not found" });
        }

        try
        {
            var result = await githubService.SyncSourceAsync(id, body.Tag, body.Replace, userId, context.RequestAborted);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult? TryEncryptPat(string pat, IConfiguration config, out string? encrypted)
    {
        encrypted = null;
        var key = config["EncryptionKey"];
        if (string.IsNullOrEmpty(key))
            return Results.BadRequest(new { error = "Server encryption key not configured. Cannot store PAT." });
        encrypted = EncryptionHelper.Encrypt(pat, key);
        return null;
    }

    // --- VCS Connection Admin Handlers ---

    public static async Task<IResult> ListConnections(IVcsConnectionService connectionService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);
        var connections = await connectionService.ListConnectionsAsync();
        return Results.Ok(connections);
    }

    public static async Task<IResult> CreateConnection(IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var body = await request.ReadFromJsonAsync<CreateConnectionRequest>();
        if (body == null || string.IsNullOrWhiteSpace(body.Label))
            return Results.BadRequest(new { error = "label is required" });

        string? patEncrypted = null;
        if (!string.IsNullOrEmpty(body.Pat))
        {
            var error = TryEncryptPat(body.Pat, config, out patEncrypted);
            if (error != null) return error;
        }

        var webhookSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var connection = await connectionService.CreateConnectionAsync(
            userId, body.Label, body.Provider ?? "github", patEncrypted, body.DefaultOrg, webhookSecret);

        context.FireAuditLog(auditService, "vcs_connection.created", "vcs_connection", connection.Id.ToString(), new { label = body.Label });

        return Results.Created($"/api/admin/vcs-connections/{connection.Id}", new
        {
            connection.Id,
            connection.Label,
            connection.Provider,
            connection.DefaultOrg,
            connection.IsActive,
            connection.CreatedAt,
            webhookSecret,
            webhookUrl = $"{config["BaseUrl"]?.TrimEnd('/')}/api/vcs/github/webhook"
        });
    }

    public static async Task<IResult> UpdateConnection(Guid id, IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var body = await request.ReadFromJsonAsync<UpdateConnectionRequest>();

        string? patEncrypted = null;
        if (!string.IsNullOrEmpty(body?.Pat))
        {
            var error = TryEncryptPat(body.Pat, config, out patEncrypted);
            if (error != null) return error;
        }

        var updated = await connectionService.UpdateConnectionAsync(id, body?.Label, patEncrypted, body?.DefaultOrg, body?.IsActive);
        if (updated == null) return Results.NotFound(new { error = "VCS connection not found" });

        context.FireAuditLog(auditService, "vcs_connection.updated", "vcs_connection", id.ToString());
        return Results.Ok(updated);
    }

    public static async Task<IResult> DeleteConnection(Guid id, IVcsConnectionService connectionService, IAuditService auditService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await connectionService.DeleteConnectionAsync(id);
        if (!result) return Results.BadRequest(new { error = "Cannot delete — connection is referenced by active VCS sources, or not found" });

        context.FireAuditLog(auditService, "vcs_connection.deleted", "vcs_connection", id.ToString());
        return Results.NoContent();
    }

    public static async Task<IResult> ListConnectionSummaries(IVcsConnectionService connectionService, HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        if (!context.User.HasPermission(Permissions.VcsManage))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);
        var connections = await connectionService.ListConnectionSummariesAsync();
        return Results.Ok(connections.Select(c => new { c.Id, c.Label, c.Provider, c.DefaultOrg }));
    }

    // --- GitHub Webhook ---

    public static async Task<IResult> HandleGitHubWebhook(IGitHubVcsService githubService, HttpContext context)
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

public record CreateVcsSourceRequest(string Namespace, string Name, string Provider, string RepoOwner, string RepoName, Guid ConnectionId, bool SyncExistingTags = false);
public record UpdateVcsSourceRequest(string? RepoOwner, string? RepoName, Guid? ConnectionId, bool? IsActive);
public record SyncVcsSourceRequest(string? Tag, bool Replace);
public record CreateConnectionRequest(string Label, string? Provider, string? Pat, string? DefaultOrg);
public record UpdateConnectionRequest(string? Label, string? Pat, string? DefaultOrg, bool? IsActive);
