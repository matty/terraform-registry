using System.Security.Claims;
using TerraformRegistry.Services;

namespace TerraformRegistry.Middleware;

/// <summary>
/// Middleware that validates JWT session tokens for portal routes.
/// API routes (/v1/*) bypass this and use API key authentication instead.
/// </summary>
public class PortalAuthenticationMiddleware(
    RequestDelegate next,
    JwtService jwtService,
    ILogger<PortalAuthenticationMiddleware> logger,
    IHostEnvironment environment,
    IConfiguration configuration)
{
    private const string SessionCookieName = "tf-session";

    // Routes that require portal authentication
    private static readonly string[] ProtectedPortalPaths = ["/modules"];

    // Routes that bypass auth; login/callback flows are explicitly skipped so auth endpoints can set Context.User.
    private static readonly string[] PublicPaths =
        ["/", "/login", "/callback", "/api/auth/login", "/api/auth/callback", "/api/auth/providers"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Dev bypass - skip all auth in dev mode
        if (environment.IsDevelopment() && IsDevAuthBypassEnabled())
        {
            var devUser = GetDevUserPrincipal();
            context.User = devUser;
            logger.LogWarning("DEV AUTH BYPASS (Portal): Auto-authenticated as dev user for {Path}", path);
            await next(context);
            return;
        }

        // Skip auth for API routes handled by API key middleware; portal uses cookie auth.
        if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/keys", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Skip auth for public paths
        if (IsPublicPath(path))
        {
            await next(context);
            return;
        }

        // For any other path (including /api/auth/me, /settings, etc), try to authenticate if cookie exists.
        // We don't enforce it unless it's a protected path, but we should populate Context.User.
        var token = context.Request.Cookies[SessionCookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var principal = jwtService.ValidateToken(token);
            if (principal != null)
            {
                context.User = principal;
                logger.LogInformation("Portal session validated for {Path}. User: {User}", path,
                    principal.Identity?.Name);
            }
        }

        // Check for protected portal paths
        if (IsProtectedPortalPath(path))
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                // For API requests, return 401; for page requests, redirect to login
                if (context.Request.Headers.Accept.ToString().Contains("application/json"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Not authenticated" });
                    return;
                }

                context.Response.Redirect("/login");
                return;
            }
        }

        await next(context);
    }

    private static bool IsPublicPath(string path)
    {
        return PublicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                                    path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProtectedPortalPath(string path)
    {
        return ProtectedPortalPaths.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStaticFile(string path)
    {
        var extensions = new[]
            { ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".map" };
        return extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
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
