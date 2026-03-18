using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

// ReSharper disable AccessToModifiedClosure

namespace TerraformRegistry.Handlers;

public static class AdminHandlers
{
    // --- Role Management (require Permissions.AdminRoles) ---

    public static async Task<IResult> ListRoles(IRoleService roleService, HttpContext context)
    {
        if (!context.User.HasPermission(Permissions.AdminRoles))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var roles = await roleService.ListRolesAsync();
        return Results.Ok(roles);
    }

    public static async Task<IResult> CreateRole(IRoleService roleService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (!context.User.HasPermission(Permissions.AdminRoles))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var body = await request.ReadFromJsonAsync<CreateRoleRequest>();
        if (body == null || string.IsNullOrWhiteSpace(body.Name) || body.Permissions == null || body.Permissions.Length == 0)
            return Results.BadRequest(new { error = "name and permissions are required" });

        // Validate that all permissions are known
        var invalidPermissions = body.Permissions.Where(p => !Permissions.All.Contains(p)).ToArray();
        if (invalidPermissions.Length > 0)
            return Results.BadRequest(new { error = $"Invalid permissions: {string.Join(", ", invalidPermissions)}" });

        var role = await roleService.CreateRoleAsync(body.Name, body.Description, body.Permissions);

        var auditUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var auditIp = context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try { await auditService.LogAsync(auditUserId, "role.created", "role", role.Id.ToString(), new { name = body.Name, permissions = body.Permissions }, auditIp); }
            catch { /* audit is non-critical */ }
        });

        return Results.Created($"/api/admin/roles/{role.Id}", role);
    }

    public static async Task<IResult> UpdateRole(Guid id, IRoleService roleService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (!context.User.HasPermission(Permissions.AdminRoles))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var body = await request.ReadFromJsonAsync<UpdateRoleRequest>();
        if (body == null)
            return Results.BadRequest(new { error = "Request body is required" });

        // Validate permissions if provided
        if (body.Permissions != null)
        {
            var invalidPermissions = body.Permissions.Where(p => !Permissions.All.Contains(p)).ToArray();
            if (invalidPermissions.Length > 0)
                return Results.BadRequest(new { error = $"Invalid permissions: {string.Join(", ", invalidPermissions)}" });
        }

        var role = await roleService.UpdateRoleAsync(id, body.Name, body.Description, body.Permissions);
        if (role == null)
            return Results.NotFound(new { error = "Role not found" });

        var auditUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var auditIp = context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try { await auditService.LogAsync(auditUserId, "role.updated", "role", id.ToString(), new { name = body.Name, permissions = body.Permissions }, auditIp); }
            catch { /* audit is non-critical */ }
        });

        return Results.Ok(role);
    }

    public static async Task<IResult> DeleteRole(Guid id, IRoleService roleService, IAuditService auditService, HttpContext context)
    {
        if (!context.User.HasPermission(Permissions.AdminRoles))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var deleted = await roleService.DeleteRoleAsync(id);
        if (!deleted)
            return Results.BadRequest(new { error = "Role not found or is a system role that cannot be deleted" });

        var auditUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var auditIp = context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try { await auditService.LogAsync(auditUserId, "role.deleted", "role", id.ToString(), null, auditIp); }
            catch { /* audit is non-critical */ }
        });

        return Results.NoContent();
    }

    // --- User Management (require Permissions.AdminUsers) ---

    public static async Task<IResult> ListUsers(IDatabaseService dbService, IPermissionService permService, HttpContext context)
    {
        if (!context.User.HasPermission(Permissions.AdminUsers))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var users = await dbService.ListAllUsersAsync();
        return Results.Ok(users);
    }

    public static async Task<IResult> GetUserRoles(string userId, IPermissionService permService, HttpContext context)
    {
        if (!context.User.HasPermission(Permissions.AdminUsers))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var roles = await permService.GetUserRolesAsync(userId);
        return Results.Ok(roles);
    }

    public static async Task<IResult> AssignUserRole(string userId, IPermissionService permService, IAuditService auditService, HttpContext context, HttpRequest request)
    {
        if (!context.User.HasPermission(Permissions.AdminUsers))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var body = await request.ReadFromJsonAsync<AssignRoleRequest>();
        if (body == null || body.RoleId == Guid.Empty)
            return Results.BadRequest(new { error = "roleId is required" });

        var assignedBy = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await permService.AssignRoleAsync(userId, body.RoleId, assignedBy);

        var auditIp = context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try { await auditService.LogAsync(assignedBy, "role.assigned", "user_role", userId, new { roleId = body.RoleId }, auditIp); }
            catch { /* audit is non-critical */ }
        });

        return Results.Ok(new { success = result });
    }

    public static async Task<IResult> RemoveUserRole(string userId, Guid roleId, IPermissionService permService, IAuditService auditService, HttpContext context)
    {
        if (!context.User.HasPermission(Permissions.AdminUsers))
            return Results.Json(new { error = "Insufficient permissions" }, statusCode: 403);

        var result = await permService.RemoveRoleAsync(userId, roleId);
        if (!result)
            return Results.NotFound(new { error = "Role assignment not found" });

        var auditUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var auditIp = context.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            try { await auditService.LogAsync(auditUserId, "role.removed", "user_role", userId, new { roleId }, auditIp); }
            catch { /* audit is non-critical */ }
        });

        return Results.NoContent();
    }
}

public record CreateRoleRequest(string Name, string? Description, string[] Permissions);
public record UpdateRoleRequest(string? Name, string? Description, string[]? Permissions);
public record AssignRoleRequest(Guid RoleId);
