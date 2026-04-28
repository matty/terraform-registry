using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TerraformRegistry.API;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ModuleDocsAdminEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Summary_WithoutReadPermission_ReturnsForbidden()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-no-read@test.com",
            "module-docs-no-read",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/api/admin/module-docs/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Summary_WithReadPermission_ReturnsConfigAndSummary()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-read@test.com",
            "module-docs-read",
            [Permissions.ModuleDocsRead]);

        var response = await client.GetAsync("/api/admin/module-docs/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("config", out var config));
        Assert.True(json.TryGetProperty("summary", out var summary));
        Assert.False(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, summary.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task UpdateConfig_WithConfigurePermission_PersistsRuntimeSetting()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-configure@test.com",
            "module-docs-configure",
            [Permissions.ModuleDocsConfigure]);

        var response = await client.PutAsJsonAsync("/api/admin/module-docs/config", new { enabled = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("enabled").GetBoolean());
        Assert.False(json.GetProperty("startupEnabled").GetBoolean());
        Assert.True(json.GetProperty("hasRuntimeOverride").GetBoolean());
    }

    [Fact]
    public async Task Backfill_WhenDisabled_ReturnsConflict()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-manage@test.com",
            "module-docs-manage",
            [Permissions.ModuleDocsManage]);

        var response = await client.PostAsJsonAsync("/api/admin/module-docs/backfill", new { limit = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
