using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
/// Service for handling OAuth2 flows with GitHub and Azure AD.
/// </summary>
public class OAuthService
{
    private readonly OidcOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthService> _logger;
    private readonly string _baseUrl;

    public OAuthService(
        OidcOptions options,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OAuthService> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = configuration["BaseUrl"] ?? "http://localhost:5131";
    }

    /// <summary>
    /// Returns list of enabled OIDC providers.
    /// </summary>
    public IEnumerable<OidcProviderInfo> GetEnabledProviders()
    {
        var providers = new List<OidcProviderInfo>();

        foreach (var (name, config) in _options.Providers)
        {
            if (!config.Enabled || string.IsNullOrEmpty(config.ClientId))
                continue;

            var normalizedName = name.ToLowerInvariant();
            providers.Add(new OidcProviderInfo
            {
                Name = normalizedName,
                DisplayName = normalizedName switch
                {
                    "github" => "GitHub",
                    "azuread" => "Azure AD",
                    _ => name
                },
                Icon = normalizedName switch
                {
                    "github" => "i-simple-icons-github",
                    "azuread" => "i-simple-icons-microsoft",
                    _ => "i-lucide-key"
                }
            });
        }

        return providers;
    }

    /// <summary>
    /// Generates the authorization URL for the specified provider.
    /// </summary>
    public string GetAuthorizationUrl(string provider, string state)
    {
        var providerKey = GetProviderKey(provider);
        if (providerKey == null || !_options.Providers.TryGetValue(providerKey, out var config))
            throw new ArgumentException($"Unknown or disabled provider: {provider}");

        if (!config.Enabled)
            throw new ArgumentException($"Provider {provider} is not enabled");

        var redirectUri = $"{_baseUrl}/api/auth/callback/{provider.ToLowerInvariant()}";
        var scopes = string.Join(" ", config.Scopes);

        return provider.ToLowerInvariant() switch
        {
            "github" => $"{config.AuthorizationEndpoint}?client_id={config.ClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scopes)}&state={state}",
            "azuread" => $"{config.AuthorizationEndpoint}?client_id={config.ClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scopes)}&state={state}&response_type=code&response_mode=query",
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    /// <summary>
    /// Exchanges an authorization code for user information.
    /// </summary>
    public async Task<UserInfo?> ExchangeCodeForUserInfoAsync(string provider, string code)
    {
        var providerKey = GetProviderKey(provider);
        if (providerKey == null || !_options.Providers.TryGetValue(providerKey, out var config))
            return null;

        var redirectUri = $"{_baseUrl}/api/auth/callback/{provider.ToLowerInvariant()}";

        return provider.ToLowerInvariant() switch
        {
            "github" => await ExchangeGitHubCodeAsync(config, code, redirectUri),
            "azuread" => await ExchangeAzureAdCodeAsync(config, code, redirectUri),
            _ => null
        };
    }

    private async Task<UserInfo?> ExchangeGitHubCodeAsync(OidcProviderOptions config, string code, string redirectUri)
    {
        var client = _httpClientFactory.CreateClient();

        // Exchange code for access token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint);
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            RegistryLog.Error(_logger, "GitHub token exchange failed: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

        if (string.IsNullOrEmpty(accessToken))
        {
            RegistryLog.Error(_logger, "GitHub did not return an access token");
            return null;
        }

        // Get user info
        var userRequest = new HttpRequestMessage(HttpMethod.Get, config.UserInfoEndpoint);
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        userRequest.Headers.Add("User-Agent", "TerraformRegistry");

        var userResponse = await client.SendAsync(userRequest);
        if (!userResponse.IsSuccessStatusCode)
        {
            RegistryLog.Error(_logger, "GitHub user info request failed: {Status}", userResponse.StatusCode);
            return null;
        }

        var userJson = await userResponse.Content.ReadAsStringAsync();
        using var userDoc = JsonDocument.Parse(userJson);
        var root = userDoc.RootElement;

        // Get email (might need separate API call if email is private)
        var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
        if (string.IsNullOrEmpty(email))
        {
            email = await GetGitHubPrimaryEmailAsync(client, accessToken);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            RegistryLog.Warning(_logger, "GitHub login rejected because no email address was available for the authenticated user.");
            return null;
        }

        return new UserInfo
        {
            Id = root.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            Email = email,
            Name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? root.GetProperty("login").GetString() ?? "" : root.GetProperty("login").GetString() ?? "",
            Provider = "github",
            AvatarUrl = root.TryGetProperty("avatar_url", out var avatarProp) ? avatarProp.GetString() ?? "" : ""
        };
    }

    private async Task<string?> GetGitHubPrimaryEmailAsync(HttpClient client, string accessToken)
    {
        try
        {
            var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
            emailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            emailRequest.Headers.Add("User-Agent", "TerraformRegistry");

            var emailResponse = await client.SendAsync(emailRequest);
            if (!emailResponse.IsSuccessStatusCode) return null;

            var emailJson = await emailResponse.Content.ReadAsStringAsync();
            using var emailDoc = JsonDocument.Parse(emailJson);

            foreach (var emailEntry in emailDoc.RootElement.EnumerateArray())
            {
                if (emailEntry.TryGetProperty("primary", out var primaryProp) &&
                    primaryProp.GetBoolean() &&
                    emailEntry.TryGetProperty("verified", out var verifiedProp) &&
                    verifiedProp.GetBoolean())
                {
                    return emailEntry.GetProperty("email").GetString();
                }
            }

            foreach (var emailEntry in emailDoc.RootElement.EnumerateArray())
            {
                if (emailEntry.TryGetProperty("verified", out var verifiedProp) &&
                    verifiedProp.GetBoolean())
                {
                    return emailEntry.GetProperty("email").GetString();
                }
            }
        }
        catch (Exception ex)
        {
            RegistryLog.Warning(_logger, "Failed to get GitHub primary email: {Message}", ex.Message);
        }

        return null;
    }

    private async Task<UserInfo?> ExchangeAzureAdCodeAsync(OidcProviderOptions config, string code, string redirectUri)
    {
        var client = _httpClientFactory.CreateClient();

        // Exchange code for access token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint);
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            RegistryLog.Error(_logger, "Azure AD token exchange failed: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

        if (string.IsNullOrEmpty(accessToken))
        {
            RegistryLog.Error(_logger, "Azure AD did not return an access token");
            return null;
        }

        // Get user info from Microsoft Graph
        var userRequest = new HttpRequestMessage(HttpMethod.Get, config.UserInfoEndpoint);
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await client.SendAsync(userRequest);
        if (!userResponse.IsSuccessStatusCode)
        {
            RegistryLog.Error(_logger, "Azure AD user info request failed: {Status}", userResponse.StatusCode);
            return null;
        }

        var userJson = await userResponse.Content.ReadAsStringAsync();
        using var userDoc = JsonDocument.Parse(userJson);
        var root = userDoc.RootElement;
        var email = root.TryGetProperty("mail", out var mailProp) ? mailProp.GetString() :
            root.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(email))
        {
            RegistryLog.Warning(_logger, "Azure AD login rejected because no email address was available for the authenticated user.");
            return null;
        }

        return new UserInfo
        {
            Id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
            Email = email,
            Name = root.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() ?? "" : "",
            Provider = "azuread",
            AvatarUrl = "" // Azure doesn't provide avatar in basic profile
        };
    }

    private static string? GetProviderKey(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "github" => "GitHub",
            "azuread" => "AzureAD",
            "azure" => "AzureAD",
            "microsoft" => "AzureAD",
            _ => null
        };
    }
}
