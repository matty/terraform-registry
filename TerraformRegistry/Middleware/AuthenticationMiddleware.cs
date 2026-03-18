using System.Security.Claims;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Middleware;

public class AuthenticationMiddleware(
    RequestDelegate next,
    string authToken,
    JwtService jwtService,
    ILogger<AuthenticationMiddleware> logger,
    IHostEnvironment environment,
    IConfiguration configuration)
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private const string SessionCookieName = "tf-session";
    private static readonly string[] ProtectedPathPrefixes = ["/v1/", "/api/keys", "/api/analytics", "/api/webhooks", "/api/vcs/sources", "/api/admin"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ProtectedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            // Dev bypass - skip all auth checks in dev mode
            if (environment.IsDevelopment() && IsDevAuthBypassEnabled())
            {
                var devUser = GetDevUserPrincipal();
                context.User = devUser;
                await LoadPermissionsIntoClaims(context);
                logger.LogWarning("DEV AUTH BYPASS: Auto-authenticated as dev user for {Path}", path);
                await next(context);
                return;
            }

            var header = context.Request.Headers[AuthorizationHeader].FirstOrDefault();

            // Check 1: Static API token (Legacy/System)
            if (!string.IsNullOrEmpty(header) && header.Equals($"{BearerPrefix}{authToken}", StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            // Check 2: Database API Keys (Try if not a JWT)
            if (!string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = header.Substring(BearerPrefix.Length);

                // Heuristic: JWTs usually have 2 dots. API keys don't.
                // If it doesn't look like a JWT, try API key validation first.
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    using var scope = context.RequestServices.CreateScope(); // Service is scoped usually
                    var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                    var result = await apiKeyService.ValidateApiKeyAsync(token);

                    if (result.IsExpired)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "API key has expired" });
                        return;
                    }

                    if (result.Key != null)
                    {
                        // Set user principal if tied to key; Terraform CLI uses ApiKey identity.
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, result.Key.UserId.ToString()),
                            new(ClaimTypes.AuthenticationMethod, "ApiKey")
                        };
                        var identity = new ClaimsIdentity(claims, "ApiKey");
                        context.User = new ClaimsPrincipal(identity);
                        await LoadPermissionsIntoClaims(context);

                        await next(context);
                        return;
                    }
                }
            }

            // Check 3: JWT from session cookie (Portal)
            var sessionToken = context.Request.Cookies[SessionCookieName];
            if (!string.IsNullOrEmpty(sessionToken))
            {
                logger.LogInformation("Processing request for {Path}. Found session cookie.", path);
                var principal = jwtService.ValidateToken(sessionToken);
                if (principal != null)
                {
                    context.User = principal;
                    await LoadPermissionsIntoClaims(context);
                    logger.LogInformation(
                        "Session cookie validated successfully for {Path}. User: {User}. IsAuthenticated: {IsAuthenticated}. AuthType: {AuthType}",
                        path,
                        principal.Identity?.Name,
                        context.User.Identity?.IsAuthenticated,
                        context.User.Identity?.AuthenticationType);

                    logger.LogInformation("AuthenticationMiddleware: Calling next middleware for {Path}", path);
                    await next(context);
                    return;
                }
                else
                {
                    logger.LogWarning("Session cookie validation failed for {Path}. Token: {TokenPrefix}...", path,
                        sessionToken.Substring(0, Math.Min(10, sessionToken.Length)));
                }
            }
            else
            {
                logger.LogInformation("Processing request for {Path}. No session cookie found.", path);
            }

            // Check 4: JWT in Authorization header (Bearer token)
            if (!string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var jwtToken = header.Substring(BearerPrefix.Length);
                // If we are here, it might be a JWT (or an invalid API key)
                // Only try JWT validation if it looks like one
                if (jwtToken.Contains('.') && jwtToken.Count(c => c == '.') == 2)
                {
                    var principal = jwtService.ValidateToken(jwtToken);
                    if (principal != null)
                    {
                        context.User = principal;
                        await LoadPermissionsIntoClaims(context);
                        await next(context);
                        return;
                    }
                }
            }

            // No valid authentication found
            logger.LogWarning("Unauthorized request to {Path} from {RemoteIp}", path,
                context.Connection.RemoteIpAddress);

            // For /api/keys, we let the [Authorize] attribute handle the challenge
            if (path.StartsWith("/api/keys", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            var accept = context.Request.Headers["Accept"].ToString();
            var prefersJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                              accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (prefersJson)
            {
                await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", path });
            }
            else
            {
                await context.Response.WriteAsync("Unauthorized: missing or invalid Authorization token.");
            }

            return;
        }

        await next(context);
    }

    /// <summary>
    /// Loads the user's RBAC permissions from the database and adds them as claims.
    /// If the user has no roles yet, assigns the default role first (lazy bootstrap).
    /// </summary>
    private static async Task LoadPermissionsIntoClaims(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        using var permScope = context.RequestServices.CreateScope();
        var permService = permScope.ServiceProvider.GetRequiredService<IPermissionService>();
        var perms = await permService.GetUserPermissionsAsync(userId);

        // Lazy bootstrap: if the user has no permissions (no roles assigned yet),
        // assign the default role and reload. This handles API-key-only users who
        // never went through the login flow.
        if (perms.Length == 0)
        {
            await permService.EnsureDefaultRoleAsync(userId);
            perms = await permService.GetUserPermissionsAsync(userId);
        }

        if (context.User.Identity is ClaimsIdentity identity)
        {
            foreach (var perm in perms)
                identity.AddClaim(new Claim("permission", perm));
        }
    }

    /// <summary>
    /// Reads the DevAuthBypass config flag.
    /// </summary>
    private bool IsDevAuthBypassEnabled()
    {
        var devBypass = configuration["DevAuthBypass"];
        return !string.IsNullOrEmpty(devBypass) &&
               bool.TryParse(devBypass, out var enabled) && enabled;
    }

    /// <summary>
    /// Builds a ClaimsPrincipal for local dev use.
    /// </summary>
    private ClaimsPrincipal GetDevUserPrincipal()
    {
        var devUserId = configuration["DevAuthBypass:UserId"] ?? "dev-user-001";
        var devEmail = configuration["DevAuthBypass:Email"] ?? "dev@localhost";
        var devName = configuration["DevAuthBypass:Name"] ?? "Dev User";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, devUserId),
            new(ClaimTypes.Email, devEmail),
            new(ClaimTypes.Name, devName),
            new(ClaimTypes.AuthenticationMethod, "DevBypass")
        };

        var identity = new ClaimsIdentity(claims, "DevBypass");
        return new ClaimsPrincipal(identity);
    }
}
