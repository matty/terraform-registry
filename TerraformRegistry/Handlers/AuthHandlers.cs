using System.Security.Claims;
using TerraformRegistry.API.Interfaces;
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
    public static IResult Login(string provider, OAuthService oauthService, HttpContext context)
    {
        try
        {
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
        HttpContext context,
        ILogger<Program> logger)
    {
        // Check for OAuth errors
        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("OAuth error from {Provider}: {Error}", provider, error);
            return Results.Redirect("/login?error=oauth_denied");
        }

        // Validate state to prevent CSRF
        var storedState = context.Request.Cookies[StateCookieName];
        if (string.IsNullOrEmpty(state) || storedState != state)
        {
            logger.LogWarning("OAuth state mismatch for {Provider}", provider);
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
            logger.LogError("Failed to exchange code for user info with {Provider}", provider);
            return Results.Redirect("/login?error=exchange_failed");
        }

        logger.LogInformation("User {Email} logged in via {Provider}", userInfo.Email, provider);

        // Ensure user exists; userInfo.Id is the provider's ID (e.g. GitHub numeric ID).
        var user = await apiKeyService.GetOrCreateUserAsync(userInfo.Email, provider, userInfo.Id);

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

        return Results.Redirect("/");
    }

    /// <summary>
    /// Returns current user info from session.
    /// </summary>
    public static IResult GetCurrentUser(JwtService jwtService, HttpContext context)
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

        return Results.Ok(userInfo);
    }

    /// <summary>
    /// Logs out the current user by clearing the session cookie.
    /// </summary>
    public static IResult Logout(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookieName);
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
        IDatabaseService dbService)
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

        return Results.Ok();
    }

    /// <summary>
    /// Creates a dev session (Development only, requires DevAuthBypass).
    /// </summary>
    public static IResult DevLogin(
        JwtService jwtService,
        IConfiguration configuration,
        IHostEnvironment environment,
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

        return Results.Ok(new
        {
            message = "Dev session created",
            user = new { id = devUserId, email = devEmail, name = devName, provider = "DevBypass" }
        });
    }
}

