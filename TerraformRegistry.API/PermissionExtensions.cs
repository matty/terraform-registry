using System.Security.Claims;

namespace TerraformRegistry.API;

public static class PermissionExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, string permission)
        => user.HasClaim("permission", permission);

    public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] permissions)
        => permissions.Any(p => user.HasClaim("permission", p));
}
