using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ApiKeyExpirationTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task ExpiredApiKeyReturns401WithExpiredMessage()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "expired-test@example.com",
            Provider = "test",
            ProviderId = "test-expired",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);
        await permissionService.EnsureDefaultRoleAsync(user.Id);

        var (rawToken, apiKey) = await apiKeyService.CreateApiKeyAsync(user.Id, "expired key");
        apiKey.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await dbService.UpdateApiKeyAsync(apiKey);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/v1/modules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("expired", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiKeyForUserWithoutRolesReturnsForbidden()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "zero-role-test@example.com",
            Provider = "test",
            ProviderId = "test-zero-role",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);

        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "zero role key");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DisabledUserApiKeyReturnsUnauthorizedImmediately()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "disabled-key-test@example.com",
            Provider = "test",
            ProviderId = "test-disabled-key",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);
        await permissionService.EnsureDefaultRoleAsync(user.Id);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "disabled key");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisabledUserSessionJwtReturnsUnauthorizedImmediately()
    {
        using var scope = Factory.Services.CreateScope();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "disabled-session-test@example.com",
            Provider = "test",
            ProviderId = "test-disabled-session",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"tf-session={jwtService.GenerateToken(user.Id, user.Email, "Disabled", user.Provider)}");

        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisabledUserPortalSessionIsRedirectedToLogin()
    {
        using var scope = Factory.Services.CreateScope();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "disabled-portal-test@example.com",
            Provider = "test",
            ProviderId = "test-disabled-portal",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);

        var client = Factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", $"tf-session={jwtService.GenerateToken(user.Id, user.Email, "Disabled", user.Provider)}");

        var response = await client.GetAsync("/modules");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ValidApiKeyWithFutureExpirationSucceeds()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "future-test@example.com",
            Provider = "test",
            ProviderId = "test-future",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);
        await permissionService.EnsureDefaultRoleAsync(user.Id);

        var (rawToken, apiKey) = await apiKeyService.CreateApiKeyAsync(user.Id, "future key");
        apiKey.ExpiresAt = DateTime.UtcNow.AddDays(30);
        await dbService.UpdateApiKeyAsync(apiKey);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/v1/modules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidApiKeyWithNoExpirationSucceeds()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "noexpiry-test@example.com",
            Provider = "test",
            ProviderId = "test-noexpiry",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbService.AddUserAsync(user);
        await permissionService.EnsureDefaultRoleAsync(user.Id);

        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "no expiry key");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/v1/modules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
