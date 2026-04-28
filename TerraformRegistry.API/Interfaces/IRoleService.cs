using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IRoleService
{
    Task<IEnumerable<Role>> ListRolesAsync();
    Task<Role?> GetRoleAsync(Guid id);
    Task<Role> CreateRoleAsync(string name, string? description, string[] permissions);
    Task<Role?> UpdateRoleAsync(Guid id, string? name, string? description, string[]? permissions);
    Task<bool> DeleteRoleAsync(Guid id);
    Task SeedDefaultRolesAsync();
}
