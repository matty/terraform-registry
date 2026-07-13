namespace TerraformRegistry.Models;

/// <summary>
/// Configuration options for a single OIDC provider.
/// </summary>
public class OidcProviderOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string UserInfoEndpoint { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public bool Enabled { get; set; }
}

/// <summary>
/// Configuration options for OIDC authentication.
/// </summary>
public class OidcOptions
{
    public string JwtSecretKey { get; set; } = string.Empty;
    public int JwtExpiryHours { get; set; } = 24;
    public Dictionary<string, OidcProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Response model for available OIDC providers.
/// </summary>
public class OidcProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

/// <summary>
/// Response model for current user info.
/// </summary>
public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}
