using System.Security.Claims;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Services;

namespace TerraformRegistry.Middleware;

public class AuthenticationMiddleware(
    RequestDelegate next,
    string authToken,
    JwtService jwtService,
    ILogger<AuthenticationMiddleware> logger,
    IHostEnvironment environment,
    IConfiguration configuration,
    IMirrorConfigService mirrorConfigService)
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";
    private const string SessionCookieName = "tf-session";
    private static readonly string[] ProtectedPathPrefixes = ["/v1/", "/api/keys", "/api/analytics", "/api/providers", "/api/vcs/sources", "/api/vcs/connections", "/api/admin"];
    private static readonly string[] StaticTokenPathPrefixes = ["/v1/"];
    private static readonly string[] StaticTokenPermissions =
    [
        Permissions.ModulesRead,
        Permissions.ModulesUpload,
        Permissions.ModulesDelete,
        Permissions.ModulesRestore,
        Permissions.ModulesPurge,
        Permissions.ModulesDescription,
        Permissions.ProvidersRead,
        Permissions.ProvidersPublish,
        Permissions.ProvidersDelete,
        Permissions.ProvidersPurge,
        Permissions.ProvidersKeysManage,
        Permissions.ProvidersDescription,
        Permissions.MirrorRead
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isMirrorProviderMetadataPath = IsMirrorProviderMetadataPath(path);
        var requiresAuthentication =
            ProtectedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            await MirrorProviderMetadataRequiresAuthenticationAsync(isMirrorProviderMetadataPath, context.RequestAborted);
        if (requiresAuthentication)
        {
            // Dev bypass - skip all auth checks in dev mode
            if (environment.IsDevelopment() && IsDevAuthBypassEnabled())
            {
                var devUser = GetDevUserPrincipal();
                context.User = devUser;
                await LoadPermissionsIntoClaims(context);
                RegistryLog.Warning(logger, "DEV AUTH BYPASS: Auto-authenticated as dev user for {Path}", path);
                await next(context);
                return;
            }

            var header = context.Request.Headers[AuthorizationHeader].FirstOrDefault();

            // Check 1: Static API token (Legacy/System)
            if (!string.IsNullOrEmpty(header) && header.Equals($"{BearerPrefix}{authToken}", StringComparison.Ordinal))
            {
                if (!StaticTokenPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                    !isMirrorProviderMetadataPath)
                {
                    RegistryLog.Warning(logger, "Static token rejected for non-module path {Path} from {RemoteIp}", path,
                        context.Connection.RemoteIpAddress);
                    await WriteUnauthorizedResponseAsync(context, path);
                    return;
                }

                context.User = GetStaticTokenPrincipal();
                await next(context);
                return;
            }

            // Check 2: Database API Keys (Try if not a JWT)
            if (!string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = header.Substring(BearerPrefix.Length);

                // Heuristic: JWTs usually have 2 dots. API keys don't.
                // If it doesn't look like a JWT, try API key validation first.
                if (!token.Contains('.', StringComparison.Ordinal) || token.Count(c => c == '.') != 2)
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
                RegistryLog.Information(logger, "Processing request for {Path}. Found session cookie.", path);
                var principal = jwtService.ValidateToken(sessionToken);
                if (principal != null)
                {
                    if (!await IsCurrentUserActiveAsync(context, principal))
                    {
                        await WriteUnauthorizedResponseAsync(context, path);
                        return;
                    }

                    context.User = principal;
                    await LoadPermissionsIntoClaims(context);
                    RegistryLog.Information(logger,
                        "Session cookie validated successfully for {Path}. User: {User}. IsAuthenticated: {IsAuthenticated}. AuthType: {AuthType}",
                        path,
                        principal.Identity?.Name,
                        context.User.Identity?.IsAuthenticated,
                        context.User.Identity?.AuthenticationType);

                    RegistryLog.Information(logger, "AuthenticationMiddleware: Calling next middleware for {Path}", path);
                    await next(context);
                    return;
                }
                else
                {
                    RegistryLog.Warning(logger, "Session cookie validation failed for {Path}. Token: {TokenPrefix}...", path,
                        sessionToken.Substring(0, Math.Min(10, sessionToken.Length)));
                }
            }
            else
            {
                RegistryLog.Information(logger, "Processing request for {Path}. No session cookie found.", path);
            }

            // Check 4: JWT in Authorization header (Bearer token). The validator receives an empty token for
            // non-bearer requests, so request-controlled format checks never guard token validation.
            var jwtToken = !string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? header.Substring(BearerPrefix.Length)
                : string.Empty;
            var jwtPrincipal = jwtService.ValidateToken(jwtToken);
            if (jwtPrincipal != null)
            {
                if (!await IsCurrentUserActiveAsync(context, jwtPrincipal))
                {
                    await WriteUnauthorizedResponseAsync(context, path);
                    return;
                }

                context.User = jwtPrincipal;
                await LoadPermissionsIntoClaims(context);
                await next(context);
                return;
            }

            // No valid authentication found
            RegistryLog.Warning(logger, "Unauthorized request to {Path} from {RemoteIp}", path,
                context.Connection.RemoteIpAddress);

            // For /api/keys, we let the [Authorize] attribute handle the challenge
            if (path.StartsWith("/api/keys", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            await WriteUnauthorizedResponseAsync(context, path);

            return;
        }

        await next(context);
    }

    /// <summary>
    /// Loads the user's RBAC permissions from the database and adds them as claims.
    /// </summary>
    private static async Task LoadPermissionsIntoClaims(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        using var permScope = context.RequestServices.CreateScope();
        var permService = permScope.ServiceProvider.GetRequiredService<IPermissionService>();
        var perms = await permService.GetUserPermissionsAsync(userId);

        if (context.User.Identity is ClaimsIdentity identity)
        {
            foreach (var perm in perms)
                identity.AddClaim(new Claim("permission", perm));
        }
    }

    private static async Task<bool> IsCurrentUserActiveAsync(HttpContext context, ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        using var scope = context.RequestServices.CreateScope();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        return (await dbService.GetUserByIdAsync(userId))?.IsActive == true;
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

    private async Task<bool> MirrorProviderMetadataRequiresAuthenticationAsync(
        bool isMirrorProviderMetadataPath,
        CancellationToken cancellationToken)
    {
        if (!isMirrorProviderMetadataPath)
        {
            return false;
        }

        var mirror = await mirrorConfigService.GetConfigAsync(cancellationToken);
        return mirror.Effective.Enabled &&
               mirror.Effective.Providers.Enabled &&
               mirror.Effective.Providers.RequireAuthentication;
    }

    private static bool IsMirrorProviderMetadataPath(string path)
    {
        if (!path.StartsWith("/mirror/providers/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 6 ||
            !string.Equals(segments[0], "mirror", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], "providers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filename = segments[5];
        return string.Equals(filename, "index.json", StringComparison.OrdinalIgnoreCase) ||
               filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
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

    private static ClaimsPrincipal GetStaticTokenPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "static-token"),
            new(ClaimTypes.AuthenticationMethod, "StaticToken")
        };

        foreach (var permission in StaticTokenPermissions)
            claims.Add(new Claim("permission", permission));

        var identity = new ClaimsIdentity(claims, "StaticToken");
        return new ClaimsPrincipal(identity);
    }

    private static async Task WriteUnauthorizedResponseAsync(HttpContext context, string path)
    {
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
    }
}
