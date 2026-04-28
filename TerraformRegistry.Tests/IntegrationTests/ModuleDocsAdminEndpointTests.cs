using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.ModuleExtraction;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ModuleDocsAdminEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    protected override void ConfigureTestApp(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var extractionHostedServices = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(ModuleExtractionHostedService))
                .ToList();

            foreach (var descriptor in extractionHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

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

    [Fact]
    public async Task Requeue_WhenEnabled_ReturnsAcceptedAndMarksPendingWithoutClearingError()
    {
        var client = await CreateModuleDocsAdminClientAsync("module-docs-requeue@test.com", "module-docs-requeue");
        await EnableExtractionAsync(client);
        await SeedModuleAsync(
            "acme",
            "network",
            "aws",
            "1.0.0",
            new ModuleExtractionState { Status = "failed", Error = "tool missing" });

        var response = await client.PostAsync("/api/admin/module-docs/modules/acme/network/aws/1.0.0/requeue", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("queued").GetBoolean());

        var detailResponse = await client.GetAsync("/api/admin/module-docs/modules/acme/network/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", detail.GetProperty("status").GetString());
        Assert.Equal("tool missing", detail.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Backfill_WhenEnabled_QueuesBoundedModulesAndMarksThemPending()
    {
        var client = await CreateModuleDocsAdminClientAsync("module-docs-backfill@test.com", "module-docs-backfill");
        await EnableExtractionAsync(client);
        await SeedModuleAsync(
            "acme",
            "failed",
            "aws",
            "1.0.0",
            new ModuleExtractionState { Status = "failed", Error = "tool missing" },
            publishedAt: DateTime.UtcNow.AddMinutes(-2));
        await SeedModuleAsync(
            "acme",
            "missing",
            "aws",
            "1.0.0",
            new ModuleExtractionState { Status = "pending" },
            publishedAt: DateTime.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync("/api/admin/module-docs/backfill", new { limit = 1 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("queued").GetInt32());
        var modules = result.GetProperty("modules");
        Assert.Equal(1, modules.GetArrayLength());
        Assert.Equal("failed", modules[0].GetProperty("name").GetString());

        var detailResponse = await client.GetAsync("/api/admin/module-docs/modules/acme/failed/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", detail.GetProperty("status").GetString());
        Assert.Equal("tool missing", detail.GetProperty("error").GetString());
    }

    private Task<HttpClient> CreateModuleDocsAdminClientAsync(string email, string providerId)
    {
        return CreateClientWithPermissionsAsync(
            email,
            providerId,
            [Permissions.ModuleDocsRead, Permissions.ModuleDocsManage, Permissions.ModuleDocsConfigure]);
    }

    private static async Task EnableExtractionAsync(HttpClient client)
    {
        var response = await client.PutAsJsonAsync("/api/admin/module-docs/config", new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedModuleAsync(
        string @namespace,
        string name,
        string provider,
        string version,
        ModuleExtractionState extraction,
        DateTime? publishedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var inserted = await database.AddModuleAsync(new ModuleStorage
        {
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            Description = $"{name} module",
            FilePath = $"/tmp/{name}.zip",
            PublishedAt = publishedAt ?? DateTime.UtcNow,
            Dependencies = [],
            Metadata = new ModuleArtifactMetadata { Extraction = extraction }
        });

        Assert.True(inserted);
    }
}
