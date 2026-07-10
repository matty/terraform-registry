using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class TerraformLoginTokenTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task TerraformTokenWithValidCodeAndPkceReturnsNewApiTokenValidForModules()
    {
        var (sessionClient, userId) = await CreateClientWithPortalSessionAsync("cli-user@example.com");
        var verifier = "verifier-secret-123456789";
        var challenge = CreatePkceChallenge(verifier);
        const string redirectUri = "http://127.0.0.1:10000/";

        var authorize = await sessionClient.GetAsync(
            $"/api/auth/terraform/authorize?client_id=terraform-cli&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&state=abc&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256");

        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.NotNull(authorize.Headers.Location);
        Assert.StartsWith(redirectUri, authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var code = GetQueryParameter(authorize.Headers.Location, "code");
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Equal("abc", GetQueryParameter(authorize.Headers.Location, "state"));

        var tokenClient = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var tokenResponse = await tokenClient.PostAsync(
            "/api/auth/terraform/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "terraform-cli",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri
            }));

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.Equal("Bearer", tokenDoc.RootElement.GetProperty("token_type").GetString());

        var modulesClient = Factory.CreateClient();
        modulesClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var modulesResponse = await modulesClient.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.OK, modulesResponse.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var keys = (await apiKeyService.ListApiKeysAsync(userId)).ToList();

        Assert.Single(keys);
        Assert.False(keys[0].IsShared);
        Assert.True(keys[0].ExpiresAt.HasValue);
        Assert.InRange(keys[0].ExpiresAt!.Value, DateTime.UtcNow.AddDays(89), DateTime.UtcNow.AddDays(91));
    }

    [Fact]
    public async Task RepeatedTerraformLoginsIssueDistinctTokens()
    {
        var first = await CompleteTerraformLoginAsync("repeat-user@example.com");
        var second = await CompleteTerraformLoginAsync("repeat-user@example.com");

        Assert.NotEqual(first, second);
    }

    private async Task<(HttpClient Client, string UserId)> CreateClientWithPortalSessionAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", Guid.NewGuid().ToString("N"));
        await permissionService.EnsureDefaultRoleAsync(user.Id);

        var jwt = jwtService.GenerateToken(user.Id, email, "CLI User", "test");
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Cookie", $"tf-session={jwt}");
        return (client, user.Id);
    }

    private async Task<string> CompleteTerraformLoginAsync(string email)
    {
        var (sessionClient, _) = await CreateClientWithPortalSessionAsync(email);
        var verifier = $"verifier-{Guid.NewGuid():N}";
        var challenge = CreatePkceChallenge(verifier);
        const string redirectUri = "http://127.0.0.1:10000/";

        var authorize = await sessionClient.GetAsync(
            $"/api/auth/terraform/authorize?client_id=terraform-cli&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&state=repeat&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256");
        var code = GetQueryParameter(authorize.Headers.Location!, "code");

        var tokenClient = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var tokenResponse = await tokenClient.PostAsync(
            "/api/auth/terraform/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "terraform-cli",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri
            }));

        var payload = await tokenResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static string CreatePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GetQueryParameter(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.Ordinal);

        return query[name];
    }
}
