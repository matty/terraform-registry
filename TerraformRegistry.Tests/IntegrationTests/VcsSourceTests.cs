using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class VcsSourceTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task VcsSourcesUnauthenticatedReturns401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/vcs/sources");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VcsSourcesCreateReturnsCreated()
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
    public async Task VcsConnectionsSummariesWithVcsManageReturnsActiveConnectionsOnly()
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
    public async Task VcsConnectionsSummariesWithoutVcsManageReturns403()
    {
        var client = await CreateClientWithPermissionsAsync(
            "vcs-summary-denied@example.com",
            "vcs-summary-denied-id",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/api/vcs/connections");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VcsSourcesCreateWithInactiveConnectionReturnsBadRequest()
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
    public async Task VcsSourcesListReturnsCreatedSource()
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
    public async Task VcsSourcesUpdateReturnsUpdated()
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
    public async Task VcsSourcesUpdateWithInactiveConnectionReturnsBadRequest()
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
    public async Task VcsSourcesDeleteReturnsNoContent()
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
    public async Task VcsSourcesCreateMissingFieldsReturnsBadRequest()
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
    public async Task VcsSourceByModuleReturnsLinkedSourceAndSyncState()
    {
        var client = await CreateAuthenticatedClientAsync("vcs-module@example.com", "vcs-module-id");

        var connectionId = await CreateTestConnectionAsync();

        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "module-ns",
            name = "module-name",
            provider = "aws",
            repoOwner = "module-owner",
            repoName = "module-repo",
            connectionId
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var response = await client.GetAsync("/api/vcs/sources/module/module-ns/module-name/aws");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("never", json.GetProperty("lastSyncStatus").GetString());
        Assert.Equal("v*", json.GetProperty("tagPattern").GetString());
    }

    [Fact]
    public async Task VcsSourceByModuleForDifferentOwnerReturnsNotFound()
    {
        var ownerClient = await CreateAuthenticatedClientAsync("vcs-module-owner@example.com", "vcs-module-owner-id");
        var otherClient = await CreateAuthenticatedClientAsync("vcs-module-other@example.com", "vcs-module-other-id");

        var connectionId = await CreateTestConnectionAsync();

        var createResponse = await ownerClient.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "owned-module-ns",
            name = "owned-module-name",
            provider = "aws",
            repoOwner = "owned-module-owner",
            repoName = "owned-module-repo",
            connectionId
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var response = await otherClient.GetAsync("/api/vcs/sources/module/owned-module-ns/owned-module-name/aws");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VcsSourcesCreateWithSyncExistingTagsReturnsSyncSummary()
    {
        var client = await CreateClientWithFakeGitHubSyncAsync(
            "vcs-sync-create@example.com",
            "vcs-sync-create-id",
            new SyncVcsSourceResult("succeeded", 2, 1, "1.2.0", null));
        var connectionId = await CreateTestConnectionAsync();

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "sync-create-ns",
            name = "sync-create-mod",
            provider = "aws",
            repoOwner = "sync-create-owner",
            repoName = "sync-create-repo",
            connectionId,
            syncExistingTags = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("sync", out var sync));
        Assert.Equal("succeeded", sync.GetProperty("status").GetString());
        Assert.Equal(2, sync.GetProperty("publishedCount").GetInt32());
    }

    [Fact]
    public async Task VcsSourcesCreateWithSyncExistingTagsFailureReturnsCreatedWithFailedSyncPayload()
    {
        var client = await CreateClientWithFakeGitHubSyncFailureAsync(
            "vcs-sync-create-failure@example.com",
            "vcs-sync-create-failure-id",
            "GitHub tag import failed");
        var connectionId = await CreateTestConnectionAsync();

        var response = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "sync-failure-ns",
            name = "sync-failure-mod",
            provider = "aws",
            repoOwner = "sync-failure-owner",
            repoName = "sync-failure-repo",
            connectionId,
            syncExistingTags = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("sync", out var sync));
        Assert.Equal("failed", sync.GetProperty("status").GetString());
        Assert.Equal("GitHub tag import failed", sync.GetProperty("error").GetString());
    }

    [Fact]
    public async Task VcsSourceSyncForDifferentOwnerReturnsNotFound()
    {
        var ownerClient = await CreateAuthenticatedClientAsync("vcs-sync-owner@example.com", "vcs-sync-owner-id");
        var otherClient = await CreateAuthenticatedClientAsync("vcs-sync-other@example.com", "vcs-sync-other-id");

        var connectionId = await CreateTestConnectionAsync();

        var createResponse = await ownerClient.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "owned-sync-ns",
            name = "owned-sync-name",
            provider = "aws",
            repoOwner = "owned-sync-owner",
            repoName = "owned-sync-repo",
            connectionId
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await otherClient.PostAsJsonAsync($"/api/vcs/sources/{id}/sync", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task VcsSourceSyncReturnsSyncSummary()
    {
        var client = await CreateClientWithFakeGitHubSyncAsync(
            "vcs-sync-success@example.com",
            "vcs-sync-success-id",
            new SyncVcsSourceResult("succeeded", 3, 1, "2.0.0", null));
        var connectionId = await CreateTestConnectionAsync();

        var createResponse = await client.PostAsJsonAsync("/api/vcs/sources", new
        {
            @namespace = "sync-success-ns",
            name = "sync-success-mod",
            provider = "aws",
            repoOwner = "sync-success-owner",
            repoName = "sync-success-repo",
            connectionId
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync($"/api/vcs/sources/{id}/sync", new
        {
            tag = "v2.0.0",
            replace = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("succeeded", json.GetProperty("status").GetString());
        Assert.Equal(3, json.GetProperty("publishedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("skippedCount").GetInt32());
        Assert.Equal("2.0.0", json.GetProperty("latestVersion").GetString());
    }

    [Fact]
    public async Task GitHubWebhookInvalidSignatureReturnsError()
    {
        // Create a VCS connection and source directly via DI so webhook lookup works
        using var scope = Factory.Services.CreateScope();
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

        var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vcs/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", "sha256=invalidsignature");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Signature verification failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubWebhookInactiveConnectionReturnsSkipped()
    {
        using var scope = Factory.Services.CreateScope();
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

        var client = Factory.CreateClient();
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
    public async Task GitHubWebhookNonTagPushReturnsSkipped()
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
        var client = Factory.CreateClient();
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
        using var scope = Factory.Services.CreateScope();
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

    private async Task<HttpClient> CreateClientWithFakeGitHubSyncAsync(string email, string providerId, SyncVcsSourceResult result)
    {
        return await CreateClientWithFakeGitHubSyncAsync(email, providerId, _ => Task.FromResult(result));
    }

    private async Task<HttpClient> CreateClientWithFakeGitHubSyncFailureAsync(string email, string providerId, string errorMessage)
    {
        return await CreateClientWithFakeGitHubSyncAsync(email, providerId, _ => throw new InvalidOperationException(errorMessage));
    }

    private async Task<HttpClient> CreateClientWithFakeGitHubSyncAsync(string email, string providerId, Func<SyncRequest, Task<SyncVcsSourceResult>> syncHandler)
    {
        var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitHubVcsService>();
                services.AddSingleton<IGitHubVcsService>(new FakeGitHubVcsService(syncHandler));
            });
        });

        using var scope = factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-key");
        var role = await roleService.CreateRoleAsync($"test-role-{Guid.NewGuid():N}", null, [Permissions.VcsManage]);
        await permissionService.AssignRoleAsync(user.Id, role.Id, null);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    private sealed record SyncRequest(Guid SourceId, string? RequestedTag, bool Replace, string? ActorUserId);

    private sealed class FakeGitHubVcsService(Func<SyncRequest, Task<SyncVcsSourceResult>> syncHandler) : IGitHubVcsService
    {
        public Task<(string Status, string? Reason, string? Version)> HandleWebhookAsync(string? signatureHeader, string? eventHeader, string body, CancellationToken cancellationToken) =>
            Task.FromResult<(string Status, string? Reason, string? Version)>(("skipped", null, null));

        public Task<SyncVcsSourceResult> SyncSourceAsync(Guid sourceId, string? requestedTag, bool replace, string? actorUserId, CancellationToken cancellationToken) =>
            syncHandler(new SyncRequest(sourceId, requestedTag, replace, actorUserId));
    }
}
