using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IPermissionService
{
    Task<string[]> GetUserPermissionsAsync(string userId);
    Task<IEnumerable<Role>> GetUserRolesAsync(string userId);
    Task<bool> AssignRoleAsync(string userId, Guid roleId, string? assignedBy);
    Task<bool> RemoveRoleAsync(string userId, Guid roleId);
    Task EnsureDefaultRoleAsync(string userId);
}
