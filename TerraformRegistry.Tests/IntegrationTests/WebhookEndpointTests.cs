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

    [Fact]
    public async Task Webhooks_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        // No auth header

        var response = await client.GetAsync("/api/webhooks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhooks_Create_ReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync("create-test@example.com", "create-test-id");

        var response = await client.PostAsJsonAsync("/api/webhooks", new
        {
            url = "https://example.com/hook",
            events = new[] { "module.uploaded" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("id", out _));
        Assert.Equal("https://example.com/hook", json.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Webhooks_List_ReturnsCreatedWebhook()
    {
        var client = await CreateAuthenticatedClientAsync("list-test@example.com", "list-test-id");

        // Create a webhook first
        await client.PostAsJsonAsync("/api/webhooks", new
        {
            url = "https://example.com/list-hook",
            events = new[] { "module.uploaded" }
        });

        // List webhooks
        var response = await client.GetAsync("/api/webhooks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("https://example.com/list-hook", body);
    }

    [Fact]
    public async Task Webhooks_Update_ReturnsUpdated()
    {
        var client = await CreateAuthenticatedClientAsync("update-test@example.com", "update-test-id");

        // Create a webhook
        var createResponse = await client.PostAsJsonAsync("/api/webhooks", new
        {
            url = "https://example.com/update-hook",
            events = new[] { "module.uploaded" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Update the webhook
        var response = await client.PutAsJsonAsync($"/api/webhooks/{id}", new
        {
            isActive = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Webhooks_Delete_ReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync("delete-test@example.com", "delete-test-id");

        // Create a webhook
        var createResponse = await client.PostAsJsonAsync("/api/webhooks", new
        {
            url = "https://example.com/delete-hook",
            events = new[] { "module.uploaded" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Delete the webhook
        var response = await client.DeleteAsync($"/api/webhooks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string providerId)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "webhook-test-key");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }
}
