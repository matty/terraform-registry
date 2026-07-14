using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class OidcSecurityTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task GetOrCreateOidcUserRejectsEmptyEmail()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            apiKeyService.GetOrCreateOidcUserAsync(string.Empty, "github", "12345"));
    }

    [Fact]
    public async Task GetOrCreateOidcUserRejectsCrossProviderEmailCollision()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        await apiKeyService.GetOrCreateOidcUserAsync("admin@example.com", "github", "gh-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            apiKeyService.GetOrCreateOidcUserAsync("admin@example.com", "azuread", "aad-1"));

        Assert.Contains("already linked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrCreateOidcUserCanonicalizesEmailBeforeCollisionChecks()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var created = await apiKeyService.GetOrCreateOidcUserAsync("Admin@Example.com", "github", "gh-1");
        var repeated = await apiKeyService.GetOrCreateOidcUserAsync("admin@example.com", "github", "gh-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            apiKeyService.GetOrCreateOidcUserAsync("ADMIN@example.com", "azuread", "aad-1"));

        Assert.Equal("admin@example.com", created.Email);
        Assert.Equal(created.Id, repeated.Id);
        Assert.Contains("already linked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrCreateOidcUserFindsLegacyMixedCaseStoredEmail()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var legacyUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "Admin@Example.com",
            Provider = "github",
            ProviderId = "gh-legacy",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };

        await dbService.AddUserAsync(legacyUser);

        var resolved = await apiKeyService.GetOrCreateOidcUserAsync("admin@example.com", "github", "gh-legacy");

        Assert.Equal(legacyUser.Id, resolved.Id);
        Assert.Equal("Admin@Example.com", resolved.Email);
    }

    [Fact]
    public async Task GetOrCreateOidcUserRejectsAmbiguousLegacyCaseVariantRows()
    {
        await InsertLegacyUserAsync("Admin@Example.com", "github", "gh-legacy-1", DateTime.UtcNow.AddDays(-10));
        await InsertLegacyUserAsync("admin@example.com", "github", "gh-legacy-2", DateTime.UtcNow.AddDays(-9));

        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            apiKeyService.GetOrCreateOidcUserAsync("ADMIN@example.com", "github", "gh-legacy-1"));

        Assert.Contains("multiple legacy user records", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrCreateOidcUserAllowsRepeatLoginForSameProviderIdentity()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var first = await apiKeyService.GetOrCreateOidcUserAsync("user@example.com", "github", "gh-1");
        var second = await apiKeyService.GetOrCreateOidcUserAsync("user@example.com", "github", "gh-1");

        Assert.Equal(first.Id, second.Id);
    }

    [Theory]
    [InlineData("/\\\\evil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/account\u0001settings")]
    [InlineData("/account%0Dsettings")]
    [InlineData("//evil.example/account")]
    [InlineData("/%2Fevil.example/account")]
    [InlineData("https://evil.example/account")]
    public async Task LoginDoesNotStoreUnsafeReturnPath(string returnTo)
    {
        using var scope = Factory.Services.CreateScope();
        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler());
        var context = CreateLoginHttpContext(scope.ServiceProvider);

        var result = AuthHandlers.Login("github", returnTo, oauthService, context);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.DoesNotContain(context.Response.Headers.SetCookie,
            value => value?.Contains("oauth-return-to=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task LoginStoresValidLocalReturnPath()
    {
        using var scope = Factory.Services.CreateScope();
        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler());
        var context = CreateLoginHttpContext(scope.ServiceProvider);

        var result = AuthHandlers.Login("github", "/account/settings?tab=security", oauthService, context);
        await result.ExecuteAsync(context);

        Assert.Contains(context.Response.Headers.SetCookie,
            value => value?.StartsWith("oauth-return-to=%2Faccount%2Fsettings%3Ftab%3Dsecurity", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CallbackIgnoresUnsafeReturnPathCookie()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler(
                CreateJsonResponse(new { access_token = "test-access-token" }),
                CreateJsonResponse(new { id = 12345, email = "user@example.com", login = "user" })));
        var context = CreateCallbackHttpContext(
            "state-return-to",
            scope.ServiceProvider,
            "oauth-return-to=/%5Cevil.example");

        var result = await AuthHandlers.Callback(
            "github",
            "auth-code",
            "state-return-to",
            null,
            oauthService,
            jwtService,
            apiKeyService,
            auditService,
            context,
            NullLogger<Program>.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task CallbackOnAccountCollisionRedirectsWithoutSessionCookie()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        await apiKeyService.GetOrCreateOidcUserAsync("admin@example.com", "azuread", "aad-1");

        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler(
                CreateJsonResponse(new { access_token = "test-access-token" }),
                CreateJsonResponse(new { id = 12345, email = "Admin@Example.com", login = "admin" })));

        var context = CreateCallbackHttpContext("state-123", scope.ServiceProvider);

        var result = await AuthHandlers.Callback(
            "github",
            "auth-code",
            "state-123",
            null,
            oauthService,
            jwtService,
            apiKeyService,
            auditService,
            context,
            NullLogger<Program>.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/login?error=account_link_required", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain(context.Response.Headers.SetCookie, value => value?.Contains("tf-session=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CallbackWhenLegacyDuplicateCaseEmailsExistRedirectsWithoutSessionCookie()
    {
        await InsertLegacyUserAsync("Admin@Example.com", "github", "gh-legacy-1", DateTime.UtcNow.AddDays(-10));
        await InsertLegacyUserAsync("admin@example.com", "azuread", "aad-legacy-2", DateTime.UtcNow.AddDays(-9));

        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler(
                CreateJsonResponse(new { access_token = "test-access-token" }),
                CreateJsonResponse(new { id = 12345, email = "ADMIN@example.com", login = "admin" })));

        var context = CreateCallbackHttpContext("state-dup", scope.ServiceProvider);

        var result = await AuthHandlers.Callback(
            "github",
            "auth-code",
            "state-dup",
            null,
            oauthService,
            jwtService,
            apiKeyService,
            auditService,
            context,
            NullLogger<Program>.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/login?error=account_link_required", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain(context.Response.Headers.SetCookie, value => value?.Contains("tf-session=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CallbackWhenOAuthExchangeProducesNoUsableEmailRedirectsWithoutSessionCookie()
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var oauthService = CreateGitHubOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler(
                CreateJsonResponse(new { access_token = "test-access-token" }),
                CreateJsonResponse(new { id = 12345, login = "admin" }),
                CreateJsonResponse(Array.Empty<object>())));

        var context = CreateCallbackHttpContext("state-456", scope.ServiceProvider);

        var result = await AuthHandlers.Callback(
            "github",
            "auth-code",
            "state-456",
            null,
            oauthService,
            jwtService,
            apiKeyService,
            auditService,
            context,
            NullLogger<Program>.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/login?error=exchange_failed", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain(context.Response.Headers.SetCookie, value => value?.Contains("tf-session=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CallbackWhenAzureAdExchangeProducesNoUsableEmailRedirectsWithoutSessionCookie()
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var oauthService = CreateAzureAdOAuthService(
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            new QueuedResponseHandler(
                CreateJsonResponse(new { access_token = "test-access-token" }),
                CreateJsonResponse(new { id = "aad-user-1", displayName = "Azure Admin", mail = "", userPrincipalName = "   " })));

        var context = CreateCallbackHttpContext("state-789", scope.ServiceProvider);

        var result = await AuthHandlers.Callback(
            "azuread",
            "auth-code",
            "state-789",
            null,
            oauthService,
            jwtService,
            apiKeyService,
            auditService,
            context,
            NullLogger<Program>.Instance);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/login?error=exchange_failed", context.Response.Headers.Location.ToString());
        Assert.DoesNotContain(context.Response.Headers.SetCookie, value => value?.Contains("tf-session=", StringComparison.Ordinal) == true);
    }

    private static OAuthService CreateGitHubOAuthService(IConfiguration configuration, HttpMessageHandler handler)
    {
        var options = new OidcOptions
        {
            Providers = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["github"] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    TokenEndpoint = "https://github.test/login/oauth/access_token",
                    UserInfoEndpoint = "https://github.test/user",
                    AuthorizationEndpoint = "https://github.test/login/oauth/authorize",
                    Scopes = ["read:user", "user:email"]
                }
            }
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://github.test")
        };

        return new OAuthService(
            options,
            new TestHttpClientFactory(client),
            configuration,
            NullLogger<OAuthService>.Instance);
    }

    private static OAuthService CreateAzureAdOAuthService(IConfiguration configuration, HttpMessageHandler handler)
    {
        var options = new OidcOptions
        {
            Providers = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["azuread"] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    TokenEndpoint = "https://login.microsoftonline.test/oauth2/v2.0/token",
                    UserInfoEndpoint = "https://graph.microsoft.test/v1.0/me",
                    AuthorizationEndpoint = "https://login.microsoftonline.test/oauth2/v2.0/authorize",
                    Scopes = ["openid", "profile", "email"]
                }
            }
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.test")
        };

        return new OAuthService(
            options,
            new TestHttpClientFactory(client),
            configuration,
            NullLogger<OAuthService>.Instance);
    }

    private async Task InsertLegacyUserAsync(string email, string provider, string providerId, DateTime createdAt)
    {
        await using var connection = new NpgsqlConnection(PostgresContainer.GetConnectionString());
        await connection.OpenAsync();

        const string sql =
            """
            INSERT INTO users (id, email, provider, provider_id, created_at, updated_at)
            VALUES (@id, @email, @provider, @providerId, @createdAt, @updatedAt)
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@provider", provider);
        command.Parameters.AddWithValue("@providerId", providerId);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        command.Parameters.AddWithValue("@updatedAt", createdAt);

        await command.ExecuteNonQueryAsync();
    }

    private static DefaultHttpContext CreateCallbackHttpContext(string state, IServiceProvider services, string? additionalCookies = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = additionalCookies is null
            ? $"oauth-state={state}"
            : $"oauth-state={state}; {additionalCookies}";
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext CreateLoginHttpContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static HttpResponseMessage CreateJsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueuedResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response available for {request.Method} {request.RequestUri}.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
