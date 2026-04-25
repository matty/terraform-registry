using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
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
    public async Task VcsSources_Create_ReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-create@example.com", "vcs-create-id");

        // Create a connection first
        var connectionId = await CreateTestConnectionAsync();

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "test-ns",
            name = "test-mod",
            provider = "aws",
            repoOwner = "test-owner",
            repoName = "test-repo",
            connectionId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("connectionId", out var connId));
        Assert.Equal(connectionId.ToString(), connId.GetString());
    }

    [Fact]
    public async Task VcsConnections_Summaries_WithVcsManage_ReturnsActiveConnectionsOnly()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-summary@example.com", "vcs-summary-id");

        await CreateTestConnectionAsync(label: "active-summary-connection");
        await CreateTestConnectionAsync(label: "inactive-summary-connection", isActive: false);

        var response = await client.GetAsync("/api/vcs/connections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("active-summary-connection", body);
        Assert.DoesNotContain("inactive-summary-connection", body);
    }

    [Fact]
    public async Task VcsConnections_Summaries_WithoutVcsManage_Returns403()
    {
        var client = await CreateClientWithPermissionsAsync(
            "vcs-summary-denied@example.com",
            "vcs-summary-denied-id",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/api/vcs/connections");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VcsSources_Create_WithInactiveConnection_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-inactive-create@example.com", "vcs-inactive-create-id");
        var connectionId = await CreateTestConnectionAsync(isActive: false);

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "inactive-ns",
            name = "inactive-mod",
            provider = "aws",
            repoOwner = "inactive-owner",
            repoName = "inactive-repo",
            connectionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VcsSources_List_ReturnsCreatedSource()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-list@example.com", "vcs-list-id");

        var connectionId = await CreateTestConnectionAsync();

        // Create a VCS source
        await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "list-ns",
            name = "list-mod",
            provider = "aws",
            repoOwner = "list-owner",
            repoName = "list-repo",
            connectionId
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

        var connectionId = await CreateTestConnectionAsync();

        // Create a VCS source
        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "upd-ns",
            name = "upd-mod",
            provider = "aws",
            repoOwner = "upd-owner",
            repoName = "upd-repo",
            connectionId
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
    public async Task VcsSources_Update_WithInactiveConnection_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-inactive-update@example.com", "vcs-inactive-update-id");

        var activeConnectionId = await CreateTestConnectionAsync(label: "active-update-connection");
        var inactiveConnectionId = await CreateTestConnectionAsync(label: "inactive-update-connection", isActive: false);

        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "upd-inactive-ns",
            name = "upd-inactive-mod",
            provider = "aws",
            repoOwner = "upd-inactive-owner",
            repoName = "upd-inactive-repo",
            connectionId = activeConnectionId
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await client.PutAsJsonAsync($"/api/vcs/sources/{id}", new
        {
            connectionId = inactiveConnectionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VcsSources_Delete_ReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-delete@example.com", "vcs-delete-id");

        var connectionId = await CreateTestConnectionAsync();

        // Create a VCS source
        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "del-ns",
            name = "del-mod",
            provider = "aws",
            repoOwner = "del-owner",
            repoName = "del-repo",
            connectionId
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
            // Missing name, provider, repoOwner, repoName, connectionId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GitHubWebhook_InvalidSignature_ReturnsError()
    {
        // Create a VCS connection and source directly via DI so webhook lookup works
        using var scope = _factory.Services.CreateScope();
        var vcsService = scope.ServiceProvider.GetRequiredService<IVcsSourceService>();
        var connectionService = scope.ServiceProvider.GetRequiredService<IVcsConnectionService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var user = await apiKeyService.GetOrCreateUserAsync("gh-sig@example.com", "test", "gh-sig-id");

        var connection = await connectionService.CreateConnectionAsync(
            user.Id, "test-connection", "github", null, null, "real-secret");

        await vcsService.CreateVcsSourceAsync(
            user.Id, "sig-ns", "sig-mod", "aws",
            "test-owner", "test-repo", connection.Id);

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
    public async Task GitHubWebhook_InactiveConnection_ReturnsSkipped()
    {
        using var scope = _factory.Services.CreateScope();
        var vcsService = scope.ServiceProvider.GetRequiredService<IVcsSourceService>();
        var connectionService = scope.ServiceProvider.GetRequiredService<IVcsConnectionService>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var user = await apiKeyService.GetOrCreateUserAsync("gh-inactive@example.com", "test", "gh-inactive-id");

        const string webhookSecret = "inactive-secret";
        var connection = await connectionService.CreateConnectionAsync(
            user.Id, "inactive-connection", "github", null, null, webhookSecret);
        await connectionService.UpdateConnectionAsync(connection.Id, null, null, null, false);

        await vcsService.CreateVcsSourceAsync(
            user.Id, "inactive-webhook-ns", "inactive-webhook-mod", "aws",
            "inactive-hook-owner", "inactive-hook-repo", connection.Id);

        var payload = JsonSerializer.Serialize(new
        {
            @ref = "refs/tags/not-semver",
            repository = new
            {
                name = "inactive-hook-repo",
                owner = new { login = "inactive-hook-owner" }
            }
        });

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vcs/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", ComputeGitHubSignature(webhookSecret, payload));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("inactive VCS connection", body, StringComparison.OrdinalIgnoreCase);
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

    private async Task<Guid> CreateTestConnectionAsync(string label = "test-connection", bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionService = scope.ServiceProvider.GetRequiredService<IVcsConnectionService>();
        var connection = await connectionService.CreateConnectionAsync(
            null, label, "github", null, null, "test-webhook-secret");
        if (!isActive)
            await connectionService.UpdateConnectionAsync(connection.Id, null, null, null, false);
        return connection.Id;
    }

    private static string ComputeGitHubSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))}";
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string providerId)
    {
        return await CreateClientWithPermissionsAsync(email, providerId, [Permissions.VcsManage]);
    }
}
