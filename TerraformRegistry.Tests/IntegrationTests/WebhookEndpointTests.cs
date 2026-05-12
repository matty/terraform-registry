using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class WebhookEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";
    private const string SafeWebhookUrl = "https://1.1.1.1/hook";
    private const string SafeListWebhookUrl = "https://1.1.1.1/list-hook";
    private const string SafeUpdateWebhookUrl = "https://1.1.1.1/update-hook";
    private const string SafeDeleteWebhookUrl = "https://1.1.1.1/delete-hook";
    private const string SafeDiscordWebhookUrl = "https://1.1.1.1/api/webhooks/123";
    private const string SafeCustomWebhookUrl = "https://1.1.1.1/custom-hook";
    private const string SafeCustomWebhookUrl2 = "https://1.1.1.1/custom-hook2";
    private const string SafeInvalidFormatWebhookUrl = "https://1.1.1.1/invalid-hook";
    private const string SafeFormatUpdateWebhookUrl = "https://1.1.1.1/fmt-update-hook";
    private static readonly string[] ModulePublishedEvents = ["module.published"];

    [Fact]
    public async Task WebhooksUnauthenticatedReturns401()
    {
        var client = Factory.CreateClient();
        // No auth header

        var response = await client.GetAsync("/api/admin/webhooks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksCreateReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync("create-test@example.com", "create-test-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeWebhookUrl,
            events = ModulePublishedEvents
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("id", out _));
        Assert.Equal(SafeWebhookUrl, json.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("ftp://example.com/hook")]
    public async Task WebhooksCreateWithUnsafeUrlReturnsBadRequest(string url)
    {
        var client = await CreateAuthenticatedClientAsync("unsafe-webhook@example.com", "unsafe-webhook-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url,
            events = ModulePublishedEvents
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksListReturnsCreatedWebhook()
    {
        var client = await CreateAuthenticatedClientAsync("list-test@example.com", "list-test-id");

        // Create a webhook first
        await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeListWebhookUrl,
            events = ModulePublishedEvents
        });

        // List webhooks
        var response = await client.GetAsync("/api/admin/webhooks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SafeListWebhookUrl, body);
    }

    [Fact]
    public async Task WebhooksUpdateReturnsUpdated()
    {
        var client = await CreateAuthenticatedClientAsync("update-test@example.com", "update-test-id");

        // Create a webhook
        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeUpdateWebhookUrl,
            events = ModulePublishedEvents
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Update the webhook
        var response = await client.PutAsJsonAsync($"/api/admin/webhooks/{id}", new
        {
            isActive = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task WebhooksUpdateWithUnsafeUrlReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("update-unsafe@example.com", "update-unsafe-id");

        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = "https://1.1.1.1/update-hook",
            events = ModulePublishedEvents
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/admin/webhooks/{id}", new
        {
            url = "http://127.0.0.1/hook"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksUpdateWithInvalidFormatReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("update-invalid-format@example.com", "update-invalid-format-id");

        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeFormatUpdateWebhookUrl,
            events = ModulePublishedEvents
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/admin/webhooks/{id}", new
        {
            format = "invalid"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksUpdateToCustomWithoutTemplateReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("update-custom-template@example.com", "update-custom-template-id");

        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeFormatUpdateWebhookUrl,
            events = ModulePublishedEvents,
            format = "generic"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/admin/webhooks/{id}", new
        {
            format = "custom"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksDeleteReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync("delete-test@example.com", "delete-test-id");

        // Create a webhook
        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeDeleteWebhookUrl,
            events = ModulePublishedEvents
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Delete the webhook
        var response = await client.DeleteAsync($"/api/admin/webhooks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksCreateWithDiscordFormatReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync("discord-fmt@example.com", "discord-fmt-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeDiscordWebhookUrl,
            events = ModulePublishedEvents,
            format = "discord"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("discord", json.GetProperty("format").GetString());
    }

    [Fact]
    public async Task WebhooksCreateWithCustomFormatRequiresTemplate()
    {
        var client = await CreateAuthenticatedClientAsync("custom-notpl@example.com", "custom-notpl-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeCustomWebhookUrl,
            events = ModulePublishedEvents,
            format = "custom"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksCreateWithCustomFormatAndTemplateReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync("custom-tpl@example.com", "custom-tpl-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeCustomWebhookUrl2,
            events = ModulePublishedEvents,
            format = "custom",
            template = "{\"text\":\"{{event}}\"}"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("custom", json.GetProperty("format").GetString());
    }

    [Fact]
    public async Task WebhooksCreateWithInvalidFormatReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("invalid-fmt@example.com", "invalid-fmt-id");

        var response = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeInvalidFormatWebhookUrl,
            events = ModulePublishedEvents,
            format = "invalid"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksUpdateFormatReturnsUpdated()
    {
        var client = await CreateAuthenticatedClientAsync("update-fmt@example.com", "update-fmt-id");

        // Create with generic format
        var createResponse = await client.PostAsJsonAsync("/api/admin/webhooks", new
        {
            url = SafeFormatUpdateWebhookUrl,
            events = ModulePublishedEvents,
            format = "generic"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Update to slack format
        var response = await client.PutAsJsonAsync($"/api/admin/webhooks/{id}", new
        {
            format = "slack"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("slack", updated.GetProperty("format").GetString());
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string providerId)
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.API.Interfaces.IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.API.Interfaces.IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "webhook-test-key");

        // Webhooks are admin-only — assign admin role
        var roles = await roleService.ListRolesAsync();
        var adminRole = roles.First(r => r.Name == TerraformRegistry.API.RoleNames.Admin);
        await permissionService.AssignRoleAsync(user.Id, adminRole.Id, null);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }
}
