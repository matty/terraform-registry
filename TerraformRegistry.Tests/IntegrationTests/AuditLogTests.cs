using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class AuditLogTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task AuditLogUnauthenticatedReturns401()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/admin/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogWithAdminPermissionReturnsOk()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/admin/audit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("entries", out var entries));
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
        Assert.True(json.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task AuditLogFilterByActionWorks()
    {
        var client = await CreateAdminClientAsync();

        // Create a role to generate an audit event
        var createResponse = await client.PostAsJsonAsync("/api/admin/roles", new
        {
            name = $"audit-test-role-{Guid.NewGuid():N}",
            description = "Role for audit filter test",
            permissions = new[] { Permissions.ModulesRead }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var auditEntries = await WaitForAuditEntriesAsync(client);
        Assert.True(auditEntries.Count > 0, "Expected at least one role.created audit entry");

        foreach (var entry in auditEntries)
        {
            Assert.Equal("role.created", entry.GetProperty("action").GetString());
        }
    }

    private static async Task<List<JsonElement>> WaitForAuditEntriesAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (true)
        {
            var response = await client.GetAsync("/api/admin/audit?action=role.created");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var auditEntries = json.GetProperty("entries").EnumerateArray().ToList();
            if (auditEntries.Count > 0 || DateTime.UtcNow >= deadline)
                return auditEntries;

            await timer.WaitForNextTickAsync();
        }
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync($"admin-{Guid.NewGuid():N}@test.com", "test", $"admin-test-{Guid.NewGuid():N}");
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "admin-key");

        var roles = await roleService.ListRolesAsync();
        var adminRole = roles.First(r => r.Name == "admin");
        await permissionService.AssignRoleAsync(user.Id, adminRole.Id, null);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }
}
