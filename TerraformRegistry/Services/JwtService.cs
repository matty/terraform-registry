using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
/// Service for generating and validating JWT tokens for portal sessions.
/// </summary>
public class JwtService
{
    private readonly string _secretKey;
    private readonly int _expiryHours;
    private readonly ILogger<JwtService> _logger;

    public JwtService(OidcOptions options, ILogger<JwtService> logger)
    {
        _secretKey = options.JwtSecretKey;
        _expiryHours = options.JwtExpiryHours;
        _logger = logger;

        if (string.IsNullOrEmpty(_secretKey) || _secretKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT secret key must be at least 32 characters. Set 'Oidc:JwtSecretKey' in configuration.");
        }
    }

    /// <summary>
    /// Generates a JWT token for a successfully authenticated user.
    /// </summary>
    public string GenerateToken(string userId, string email, string name, string provider, string? avatarUrl = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new(ClaimTypes.Name, name), // Map to standard .NET identity name
            new("provider", provider),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrEmpty(avatarUrl))
        {
            claims.Add(new Claim("avatar_url", avatarUrl));
        }

        var token = new JwtSecurityToken(
            issuer: "terraform-registry",
            audience: "terraform-registry-portal",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expiryHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validates a JWT token and returns the claims principal if valid.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var handler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "terraform-registry",
                ValidateAudience = true,
                ValidAudience = "terraform-registry-portal",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = handler.ValidateToken(token, validationParameters, out _);

            // Explicitly ensure the identity has an AuthenticationType so IsAuthenticated is true
            if (principal.Identity is ClaimsIdentity identity && string.IsNullOrEmpty(identity.AuthenticationType))
            {
                var newIdentity = new ClaimsIdentity(identity.Claims, "Jwt");
                return new ClaimsPrincipal(newIdentity);
            }

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError("JWT validation failed: {Message}. Token: {TokenPrefix}...", ex.Message, token.Substring(0, Math.Min(10, token.Length)));
            return null;
        }
    }

    /// <summary>
    /// Extracts user info from a validated claims principal.
    /// </summary>
    public static UserInfo? GetUserInfoFromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal == null) return null;

        return new UserInfo
        {
            Id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                 ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? string.Empty,
            Email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)
                    ?? principal.FindFirstValue(ClaimTypes.Email)
                    ?? string.Empty,
            Name = principal.FindFirstValue(JwtRegisteredClaimNames.Name)
                   ?? principal.FindFirstValue(ClaimTypes.Name)
                   ?? string.Empty,
            Provider = principal.FindFirstValue("provider") ?? string.Empty,
            AvatarUrl = principal.FindFirstValue("avatar_url") ?? string.Empty
        };
    }
}
