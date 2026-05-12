using System.Net;
using System.Net.Http.Headers;
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

public class LlmEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
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
    public async Task LlmTxtReturnsDiscoveryGuide()
    {
        var response = await Factory.CreateClient().GetAsync("/llm.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("/v1/llm/modules", text);
        Assert.Contains("Authorization: Bearer <token>", text);
    }

    [Fact]
    public async Task ListModulesWithoutAuthenticationReturnsUnauthorized()
    {
        var response = await Factory.CreateClient().GetAsync("/v1/llm/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ModuleContextWithReadPermissionReturnsStoredArtifact()
    {
        var client = await CreateClientWithPermissionsAsync(
            "llm-read@test.com",
            "llm-read",
            [Permissions.ModulesRead]);

        await SeedModuleWithLlmContextAsync("acme", "network", "aws", "1.0.0");

        var listResponse = await client.GetAsync("/v1/llm/modules");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("registry-llm-index.v1", listJson.GetProperty("schemaVersion").GetString());
        Assert.True(listJson.GetProperty("modules").GetArrayLength() >= 1);

        var versionsResponse = await client.GetAsync("/v1/llm/modules/acme/network/aws");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versionsJson = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("registry-llm-module.v1", versionsJson.GetProperty("schemaVersion").GetString());
        Assert.True(versionsJson.GetProperty("versions")[0].GetProperty("llmReady").GetBoolean());

        var contextResponse = await client.GetAsync("/v1/llm/modules/acme/network/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, contextResponse.StatusCode);
        var contextJson = await contextResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("module-llm-context.v1", contextJson.GetProperty("schemaVersion").GetString());
        Assert.Equal("acme", contextJson.GetProperty("module").GetProperty("namespace").GetString());
        Assert.Equal("https://registry.example.com/modules/acme/network/aws", contextJson.GetProperty("navigation").GetProperty("humanUrl").GetString());
    }

    [Fact]
    public async Task ModuleContextWhenArtifactMissingReturnsConflict()
    {
        var client = await CreateClientWithPermissionsAsync(
            "llm-missing@test.com",
            "llm-missing",
            [Permissions.ModulesRead]);

        await SeedModuleOnlyAsync("acme", "missing", "aws", "1.0.0");

        var response = await client.GetAsync("/v1/llm/modules/acme/missing/aws/1.0.0");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task SeedModuleOnlyAsync(string @namespace, string name, string provider, string version)
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
            PublishedAt = DateTime.UtcNow,
            Dependencies = [],
            Metadata = new ModuleArtifactMetadata
            {
                Extraction = new ModuleExtractionState { Status = "succeeded" }
            }
        });

        Assert.True(inserted);
    }

    private async Task SeedModuleWithLlmContextAsync(string @namespace, string name, string provider, string version)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var inserted = await database.AddModuleAsync(new ModuleStorage
        {
            Namespace = @namespace,
            Name = name,
            Provider = provider,
            Version = version,
            Description = "Creates networking primitives.",
            FilePath = $"/tmp/{name}.zip",
            PublishedAt = DateTime.UtcNow,
            Dependencies = [],
            Metadata = new ModuleArtifactMetadata
            {
                Extraction = new ModuleExtractionState { Status = "succeeded" },
                LlmContext = new ModuleLlmContextState { Status = "succeeded" }
            }
        });

        Assert.True(inserted);

        await database.UpsertModuleLlmContextAsync(@namespace, name, provider, version, new ModuleLlmContextDocument
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = @namespace,
                Name = name,
                Provider = provider,
                Version = version
            },
            Summary = new ModuleLlmContextSummary
            {
                OneLine = "Creates networking primitives.",
                Capabilities = ["Creates VPC resources"]
            },
            Navigation = new ModuleLlmNavigationLinks
            {
                HumanUrl = "https://registry.example.com/modules/acme/network/aws",
                ModuleVersionsUrl = "https://registry.example.com/v1/llm/modules/acme/network/aws"
            }
        });
    }
}
