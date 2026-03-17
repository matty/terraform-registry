using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class VcsSourceTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task VcsSources_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/vcs/sources");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VcsSources_Create_ReturnsCreatedWithWebhookInfo()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-create@example.com", "vcs-create-id");

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "test-ns",
            name = "test-mod",
            provider = "aws",
            repoOwner = "test-owner",
            repoName = "test-repo"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("webhookSecret", out var secret));
        Assert.False(string.IsNullOrEmpty(secret.GetString()));
        Assert.True(json.TryGetProperty("webhookUrl", out var url));
        Assert.Contains("/api/vcs/github/webhook", url.GetString());
    }

    [Fact]
    public async Task VcsSources_List_ReturnsCreatedSource()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-list@example.com", "vcs-list-id");

        // Create a VCS source
        await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "list-ns",
            name = "list-mod",
            provider = "aws",
            repoOwner = "list-owner",
            repoName = "list-repo"
        });

        // List VCS sources
        var response = await client.GetAsync("/api/vcs/sources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("list-owner", body);
        Assert.Contains("list-repo", body);
    }

    [Fact]
    public async Task VcsSources_Update_ReturnsUpdated()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-update@example.com", "vcs-update-id");

        // Create a VCS source
        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "upd-ns",
            name = "upd-mod",
            provider = "aws",
            repoOwner = "upd-owner",
            repoName = "upd-repo"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Update the VCS source
        var response = await client.PutAsJsonAsync($"/api/vcs/sources/{id}", new
        {
            isActive = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task VcsSources_Delete_ReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-delete@example.com", "vcs-delete-id");

        // Create a VCS source
        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "del-ns",
            name = "del-mod",
            provider = "aws",
            repoOwner = "del-owner",
            repoName = "del-repo"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        // Delete the VCS source
        var response = await client.DeleteAsync($"/api/vcs/sources/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task VcsSources_Create_MissingFields_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-bad@example.com", "vcs-bad-id");

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "test-ns"
            // Missing name, provider, repoOwner, repoName
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GitHubWebhook_InvalidSignature_ReturnsSkipped()
    {
        // Create a VCS source directly via DI so webhook lookup works
        using var scope = _factory.Services.CreateScope();
        var vcsService = scope.ServiceProvider.GetRequiredService<IVcsSourceService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var user = await apiKeyService.GetOrCreateUserAsync("gh-sig@example.com", "test", "gh-sig-id");

        await vcsService.CreateVcsSourceAsync(
            user.Id, "sig-ns", "sig-mod", "aws",
            "test-owner", "test-repo", null, "real-secret");

        var payload = JsonSerializer.Serialize(new
        {
            @ref = "refs/tags/v1.0.0",
            repository = new
            {
                name = "test-repo",
                owner = new { login = "test-owner" }
            }
        });

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vcs/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", "sha256=invalidsignature");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Signature verification failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubWebhook_NonTagPush_ReturnsSkipped()
    {
        var payload = JsonSerializer.Serialize(new
        {
            @ref = "refs/heads/main",
            repository = new
            {
                name = "nontag-repo",
                owner = new { login = "nontag-owner" }
            }
        });

        // Compute a valid signature (won't matter since it's not a tag push, but include for completeness)
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vcs/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", "sha256=dummy");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not a tag", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string providerId)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "vcs-test-key");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }
}
