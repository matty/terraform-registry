using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

/// <summary>
/// Minimal API handlers for OIDC authentication.
/// </summary>
public static class AuthHandlers
{
    private const string SessionCookieName = "tf-session";
    private const string StateCookieName = "oauth-state";
    private const string ReturnToCookieName = "oauth-return-to";

    /// <summary>
    /// Returns list of enabled OIDC providers.
    /// </summary>
    public static IResult GetProviders(OAuthService oauthService)
    {
        var providers = oauthService.GetEnabledProviders();
        return Results.Ok(providers);
    }

    /// <summary>
    /// Initiates OIDC login flow for the specified provider.
    /// </summary>
    public static IResult Login(string provider, string? returnTo, OAuthService oauthService, HttpContext context)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(returnTo) && IsSafeReturnPath(returnTo))
            {
                context.Response.Cookies.Append(ReturnToCookieName, returnTo, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(10)
                });
            }

            // Generate and store state for CSRF protection
            var state = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(StateCookieName, state, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10)
            });

            var authUrl = oauthService.GetAuthorizationUrl(provider, state);
            return Results.Redirect(authUrl);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Handles OIDC callback after provider authentication.
    /// </summary>
    public static async Task<IResult> Callback(
        string provider,
        string? code,
        string? state,
        string? error,
        OAuthService oauthService,
        JwtService jwtService,
        IApiKeyService apiKeyService,
        IAuditService auditService,
        HttpContext context,
        ILogger<Program> logger)
    {
        // Check for OAuth errors
        if (!string.IsNullOrEmpty(error))
        {
            RegistryLog.Warning(logger, "OAuth error from {Provider}: {Error}", provider, error);
            return Results.Redirect("/login?error=oauth_denied");
        }

        // Validate state to prevent CSRF
        var storedState = context.Request.Cookies[StateCookieName];
        if (string.IsNullOrEmpty(state) || storedState != state)
        {
            RegistryLog.Warning(logger, "OAuth state mismatch for {Provider}", provider);
            return Results.Redirect("/login?error=invalid_state");
        }

        // Clear state cookie
        context.Response.Cookies.Delete(StateCookieName);

        if (string.IsNullOrEmpty(code))
        {
            return Results.Redirect("/login?error=no_code");
        }

        // Exchange code for user info
        var userInfo = await oauthService.ExchangeCodeForUserInfoAsync(provider, code);
        if (userInfo == null)
        {
            RegistryLog.Error(logger, "Failed to exchange code for user info with {Provider}", provider);
            return Results.Redirect("/login?error=exchange_failed");
        }

        RegistryLog.Information(logger, "User {Email} logged in via {Provider}", userInfo.Email, provider);

        // Ensure user exists; userInfo.Id is the provider's ID (e.g. GitHub numeric ID).
        User user;
        try
        {
            user = await apiKeyService.GetOrCreateOidcUserAsync(new OidcUserAdmission(
                userInfo.Email,
                provider,
                userInfo.Id,
                userInfo.Issuer,
                userInfo.TenantId,
                userInfo.EmailVerified));
        }
        catch (InvalidOperationException ex)
        {
            RegistryLog.Warning(logger, ex, "OIDC login rejected for provider {Provider}", provider);
            return Results.Redirect("/login?error=account_link_required");
        }

        // Assign default role (or admin if email matches AdminEmails config)
        var permService = context.RequestServices.GetRequiredService<IPermissionService>();
        await permService.EnsureDefaultRoleAsync(user.Id);

        var adminEmails = context.RequestServices.GetRequiredService<IConfiguration>()["AdminEmails"];
        if (!string.IsNullOrEmpty(adminEmails))
        {
            var emails = adminEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (emails.Contains(userInfo.Email, StringComparer.OrdinalIgnoreCase))
            {
                var roleService = context.RequestServices.GetRequiredService<IRoleService>();
                var roles = await roleService.ListRolesAsync();
                var adminRole = roles.FirstOrDefault(r => r.Name == RoleNames.Admin);
                if (adminRole != null)
                {
                    await permService.AssignRoleAsync(user.Id, adminRole.Id, "auto-admin-bootstrap");
                }
            }
        }

        // Generate session JWT using the database User ID, not the provider ID
        var token = jwtService.GenerateToken(
            user.Id, // Use DB ID
            userInfo.Email,
            userInfo.Name,
            userInfo.Provider,
            userInfo.AvatarUrl
        );

        // Set session cookie
        context.Response.Cookies.Append(SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(24)
        });

        await context.FireAuditLogAsync(auditService, "user.login", "user", user.Id, new { email = userInfo.Email, provider });

        var returnTo = context.Request.Cookies[ReturnToCookieName];
        if (!string.IsNullOrWhiteSpace(returnTo) && IsSafeReturnPath(returnTo))
        {
            context.Response.Cookies.Delete(ReturnToCookieName);
            return Results.Redirect(returnTo);
        }

        return Results.Redirect("/");
    }

    public static async Task<IResult> BeginTerraformAuthorization(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var returnTo = $"{context.Request.Path}{context.Request.QueryString}";
            return Results.Redirect($"/login?returnTo={Uri.EscapeDataString(returnTo)}");
        }

        var clientId = context.Request.Query["client_id"].ToString();
        var redirectUri = context.Request.Query["redirect_uri"].ToString();
        var responseType = context.Request.Query["response_type"].ToString();
        var state = context.Request.Query["state"].ToString();
        var codeChallenge = context.Request.Query["code_challenge"].ToString();
        var codeChallengeMethod = context.Request.Query["code_challenge_method"].ToString();

        if (!IsValidTerraformAuthorizeRequest(clientId, redirectUri, responseType, codeChallenge, codeChallengeMethod))
        {
            return Results.BadRequest(new { error = "Invalid Terraform authorization request." });
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var codeStore = context.RequestServices.GetRequiredService<ITerraformAuthorizationCodeStore>();
        var issued = codeStore.Create(new TerraformAuthorizationCodeCreateRequest(
            userId,
            clientId,
            redirectUri,
            state,
            codeChallenge,
            codeChallengeMethod));
        var auditService = context.RequestServices.GetRequiredService<IAuditService>();
        await context.FireAuditLogAsync(auditService, "terraform_cli.login.started", "user", userId, new
        {
            clientId,
            redirectUri
        });

        var redirect = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            ["code"] = issued.Code,
            ["state"] = state
        });

        return Results.Redirect(redirect);
    }

    public static async Task<IResult> ExchangeTerraformToken(
        HttpContext context,
        ITerraformAuthorizationCodeStore codeStore,
        IApiKeyService apiKeyService,
        IAuditService auditService)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected form-encoded token request." });
        }

        var form = await context.Request.ReadFormAsync();
        var grantType = form["grant_type"].ToString();
        var clientId = form["client_id"].ToString();
        var code = form["code"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var codeVerifier = form["code_verifier"].ToString();

        if (!string.Equals(grantType, "authorization_code", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(redirectUri) ||
            string.IsNullOrWhiteSpace(codeVerifier))
        {
            return Results.BadRequest(new { error = "Invalid OAuth token request." });
        }

        var issued = codeStore.Consume(code, clientId, redirectUri);
        if (issued == null)
        {
            return Results.BadRequest(new { error = "Invalid or expired authorization code." });
        }

        if (!string.Equals(issued.CodeChallengeMethod, "S256", StringComparison.Ordinal) ||
            !string.Equals(issued.CodeChallenge, ComputePkceChallenge(codeVerifier), StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Invalid PKCE code verifier." });
        }

        var host = new Uri(redirectUri).Host;
        var description = $"Terraform CLI ({host}) {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
        var (rawToken, _) = await apiKeyService.CreateExpiringApiKeyAsync(
            issued.UserId,
            description,
            DateTime.UtcNow.AddDays(90));
        await context.FireAuditLogAsync(auditService, "terraform_cli.key.created", "user", issued.UserId, new
        {
            clientId,
            redirectUri
        });

        return Results.Ok(new
        {
            access_token = rawToken,
            token_type = "Bearer"
        });
    }

    /// <summary>
    /// Returns current user info from session.
    /// </summary>
    public static async Task<IResult> GetCurrentUser(JwtService jwtService, IPermissionService permService, HttpContext context)
    {
        var token = context.Request.Cookies[SessionCookieName];
        if (string.IsNullOrEmpty(token))
        {
            return Results.Unauthorized();
        }

        var principal = jwtService.ValidateToken(token);
        var userInfo = JwtService.GetUserInfoFromPrincipal(principal);

        if (userInfo == null)
        {
            return Results.Unauthorized();
        }

        // Load permissions and roles from DB
        var permissions = await permService.GetUserPermissionsAsync(userInfo.Id);
        var roles = await permService.GetUserRolesAsync(userInfo.Id);

        return Results.Ok(new
        {
            userInfo.Id,
            userInfo.Email,
            userInfo.Name,
            userInfo.Provider,
            userInfo.AvatarUrl,
            permissions,
            roles = roles.Select(r => new { r.Id, r.Name, r.Description })
        });
    }

    /// <summary>
    /// Logs out the current user by clearing the session cookie.
    /// </summary>
    public static async Task<IResult> Logout(HttpContext context, IAuditService auditService)
    {
        context.Response.Cookies.Delete(SessionCookieName);
        await context.FireAuditLogAsync(auditService, "user.logout", "user", context.User.FindFirstValue(ClaimTypes.NameIdentifier));

        return Results.Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Checks if user has a valid session.
    /// </summary>
    public static IResult CheckSession(JwtService jwtService, HttpContext context)
    {
        var token = context.Request.Cookies[SessionCookieName];
        if (string.IsNullOrEmpty(token))
        {
            return Results.Ok(new { authenticated = false });
        }

        var principal = jwtService.ValidateToken(token);
        if (principal == null)
        {
            return Results.Ok(new { authenticated = false });
        }

        return Results.Ok(new { authenticated = true });
    }

    /// <summary>
    /// Deletes the current user's account.
    /// </summary>
    public static async Task<IResult> DeleteAccount(
        HttpContext context,
        IApiKeyService apiKeyService,
        IDatabaseService dbService,
        IAuditService auditService)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        // Check for existing API keys
        var keys = await apiKeyService.ListApiKeysAsync(userId);
        if (keys.Any())
        {
            return Results.Conflict(new { error = "You must delete all API keys before deleting your account." });
        }

        // Delete user
        await dbService.DeleteUserAsync(userId);

        // Clear session
        context.Response.Cookies.Delete(SessionCookieName);
        await context.FireAuditLogAsync(auditService, "user.deleted", "user", userId);

        return Results.Ok();
    }

    /// <summary>
    /// Creates a dev session (Development only, requires DevAuthBypass).
    /// </summary>
    public static async Task<IResult> DevLogin(
        JwtService jwtService,
        IConfiguration configuration,
        IHostEnvironment environment,
        IAuditService auditService,
        HttpContext context)
    {
        // Never allow this in prod
        if (!environment.IsDevelopment())
        {
            return Results.NotFound();
        }

        // Bail if bypass isn't turned on
        var devBypass = configuration["DevAuthBypass"];
        if (string.IsNullOrEmpty(devBypass) || !bool.TryParse(devBypass, out var enabled) || !enabled)
        {
            return Results.BadRequest(new { error = "Dev auth bypass is not enabled. Set TF_REG_DevAuthBypass=true" });
        }

        // Dev user details (overridable via config)
        var devUserId = configuration["DevAuthBypass:UserId"] ?? "dev-user-001";
        var devEmail = configuration["DevAuthBypass:Email"] ?? "dev@localhost";
        var devName = configuration["DevAuthBypass:Name"] ?? "Dev User";

        // Ensure the dev user exists in the database (needed for FK constraints on webhooks, etc.)
        var dbService = context.RequestServices.GetRequiredService<IDatabaseService>();
        var existingUser = await dbService.GetUserByEmailAsync(devEmail);
        if (existingUser == null)
        {
            await dbService.AddUserAsync(new User
            {
                Id = devUserId,
                Email = devEmail,
                Provider = "DevBypass",
                ProviderId = devUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Assign default role (or admin if email matches AdminEmails config)
        var permService = context.RequestServices.GetRequiredService<IPermissionService>();
        await permService.EnsureDefaultRoleAsync(devUserId);

        var adminEmails = configuration["AdminEmails"];
        if (!string.IsNullOrEmpty(adminEmails))
        {
            var emails = adminEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (emails.Contains(devEmail, StringComparer.OrdinalIgnoreCase))
            {
                var roleService = context.RequestServices.GetRequiredService<IRoleService>();
                var roles = await roleService.ListRolesAsync();
                var adminRole = roles.FirstOrDefault(r => r.Name == RoleNames.Admin);
                if (adminRole != null)
                {
                    await permService.AssignRoleAsync(devUserId, adminRole.Id, "auto-admin-bootstrap");
                }
            }
        }

        // Generate session token
        var token = jwtService.GenerateToken(
            devUserId,
            devEmail,
            devName,
            "DevBypass",
            "" // No avatar for dev user
        );

        // Set session cookie
        context.Response.Cookies.Append(SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(24)
        });

        await context.FireAuditLogAsync(auditService, "user.login", "user", devUserId, new { email = devEmail, provider = "DevBypass" });

        return Results.Ok(new
        {
            message = "Dev session created",
            user = new { id = devUserId, email = devEmail, name = devName, provider = "DevBypass" }
        });
    }

    private static bool IsSafeReturnPath(string path)
    {
        return path.StartsWith('/') &&
               !path.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsValidTerraformAuthorizeRequest(string clientId, string redirectUri, string responseType,
        string codeChallenge, string codeChallengeMethod)
    {
        return string.Equals(clientId, "terraform-cli", StringComparison.Ordinal) &&
               string.Equals(responseType, "code", StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(codeChallenge) &&
               string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal) &&
               IsValidLoopbackRedirectUri(redirectUri);
    }

    private static bool IsValidLoopbackRedirectUri(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
               string.Equals(uri.Host, "::1", StringComparison.Ordinal);
    }

    private static string ComputePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
