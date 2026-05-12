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
    public async Task SummaryWithoutReadPermissionReturnsForbidden()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-no-read@test.com",
            "module-docs-no-read",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/api/admin/module-docs/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SummaryWithReadPermissionReturnsConfigAndSummary()
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
    public async Task UpdateConfigWithConfigurePermissionPersistsRuntimeSetting()
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
    public async Task BackfillWhenDisabledReturnsConflict()
    {
        var client = await CreateClientWithPermissionsAsync(
            "module-docs-manage@test.com",
            "module-docs-manage",
            [Permissions.ModuleDocsManage]);

        var response = await client.PostAsJsonAsync("/api/admin/module-docs/backfill", new { limit = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RequeueWhenEnabledReturnsAcceptedAndMarksPendingWithoutClearingError()
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
    public async Task DetailIncludesStoredLlmContextAndRegenerateEndpointRefreshesIt()
    {
        var client = await CreateModuleDocsAdminClientAsync("module-docs-llm@test.com", "module-docs-llm");

        await SeedModuleAsync(
            "acme",
            "network",
            "aws",
            "1.0.0",
            new ModuleExtractionState { Status = "succeeded" },
            new ModuleLlmContextState { Status = "failed", Error = "stale artifact" });

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

            await database.UpdateModuleDescriptionAsync("acme", "network", "aws", string.Empty);

            await database.UpsertModuleExtractionAsync("acme", "network", "aws", "1.0.0", new ModuleExtractionDocument
            {
                Readme = new ModuleReadmeDocument
                {
                    Path = "README.md",
                    Title = "Network Module",
                    Markdown = "Creates reusable networking primitives."
                },
                Inputs =
                [
                    new ModuleInputDefinition
                    {
                        Name = "name",
                        Description = "Name prefix",
                        Required = true,
                        Type = "string"
                    }
                ]
            });

            await database.UpsertModuleLlmContextAsync("acme", "network", "aws", "1.0.0", new ModuleLlmContextDocument
            {
                Summary = new ModuleLlmContextSummary
                {
                    OneLine = "outdated"
                }
            });
        }

        var detailResponse = await client.GetAsync("/api/admin/module-docs/modules/acme/network/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("outdated", detail.GetProperty("llmContext").GetProperty("summary").GetProperty("oneLine").GetString());

        var regenerateResponse = await client.PostAsync("/api/admin/module-docs/modules/acme/network/aws/1.0.0/regenerate-llm", null);
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);
        var regenerateBody = await regenerateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(regenerateBody.GetProperty("regenerated").GetBoolean());
        Assert.False(regenerateBody.GetProperty("queued").GetBoolean());

        var refreshedResponse = await client.GetAsync("/api/admin/module-docs/modules/acme/network/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, refreshedResponse.StatusCode);
        var refreshedDetail = await refreshedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Creates reusable networking primitives.", refreshedDetail.GetProperty("llmContext").GetProperty("summary").GetProperty("oneLine").GetString());
        Assert.Equal("succeeded", refreshedDetail.GetProperty("llmStatus").GetString());
    }

    [Fact]
    public async Task BackfillWhenEnabledQueuesBoundedModulesAndMarksThemPending()
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
        ModuleLlmContextState? llmContext = null,
        DateTime? publishedAt = null)
    {
        using var scope = Factory.Services.CreateScope();
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
            Metadata = new ModuleArtifactMetadata
            {
                Extraction = extraction,
                LlmContext = llmContext ?? new ModuleLlmContextState { Status = "pending" }
            }
        });

        Assert.True(inserted);
    }
}
