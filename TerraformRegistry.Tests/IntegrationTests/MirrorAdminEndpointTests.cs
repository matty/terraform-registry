using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class MirrorAdminEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task SummaryWithoutReadPermissionReturnsForbidden()
    {
        var client = await CreateClientWithPermissionsAsync(
            "mirror-admin-no-read@test.com",
            "mirror-admin-no-read",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/api/admin/mirror/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SummaryWithReadPermissionReturnsCacheCounts()
    {
        await SeedMirrorEntriesAsync();
        var client = await CreateMirrorAdminClientAsync("mirror-admin-summary@test.com", "mirror-admin-summary");

        var response = await client.GetAsync("/api/admin/mirror/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("providers").GetProperty("failed").GetInt32());
        Assert.Equal(1, json.GetProperty("modules").GetProperty("ready").GetInt32());
        Assert.Equal(2, json.GetProperty("total").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task EntriesSupportsKindAndStateFilters()
    {
        await SeedMirrorEntriesAsync();
        var client = await CreateMirrorAdminClientAsync("mirror-admin-entries@test.com", "mirror-admin-entries");

        var response = await client.GetAsync("/api/admin/mirror/entries?kind=provider&state=failed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("providers").GetArrayLength());
        Assert.Equal(0, json.GetProperty("modules").GetArrayLength());
        Assert.Equal("failed", json.GetProperty("providers")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task UpdateConfigWithConfigurePermissionPersistsRuntimeMirrorSetting()
    {
        var client = await CreateMirrorAdminClientAsync("mirror-admin-config@test.com", "mirror-admin-config");

        var response = await client.PutAsJsonAsync("/api/admin/mirror/config", new MirrorConfigUpdateRequest
        {
            Enabled = true,
            Providers = new MirrorProviderRuntimeOptions { Enabled = false },
            Modules = new MirrorModuleRuntimeOptions { Enabled = true },
            Limits = new MirrorLimitRuntimeOptions { MaxConcurrentDownloads = 2 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("hasRuntimeOverride").GetBoolean());
        Assert.True(json.GetProperty("effective").GetProperty("enabled").GetBoolean());
        Assert.False(json.GetProperty("effective").GetProperty("providers").GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("effective").GetProperty("modules").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task RetryClearsFailedStateWithoutFetchingUpstream()
    {
        await SeedMirrorEntriesAsync();
        var client = await CreateMirrorAdminClientAsync("mirror-admin-retry@test.com", "mirror-admin-retry");

        var response = await client.PostAsJsonAsync("/api/admin/mirror/retry", new MirrorAdminRetryRequest
        {
            Kind = "provider",
            Hostname = "registry.example.com",
            Namespace = "hashicorp",
            Type = "aws",
            Version = "5.0.0"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderMirrorRepository>();
        var package = await repository.GetProviderPackageAsync("registry.example.com", "hashicorp", "aws", "5.0.0", "linux", "amd64");
        Assert.NotNull(package);
        Assert.Equal("pending", package!.State);
        Assert.Null(package.LastError);
    }

    [Fact]
    public async Task DeleteModuleRemovesOnlyMirrorCacheRow()
    {
        await SeedMirrorEntriesAsync();
        var client = await CreateMirrorAdminClientAsync("mirror-admin-delete@test.com", "mirror-admin-delete");

        var response = await client.DeleteAsync("/api/admin/mirror/modules/registry.example.com/hashicorp/vpc/aws/1.2.3");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IModuleMirrorRepository>();
        var package = await repository.GetModulePackageAsync("registry.example.com", "hashicorp", "vpc", "aws", "1.2.3");
        Assert.Null(package);
    }

    private Task<HttpClient> CreateMirrorAdminClientAsync(string email, string providerId)
    {
        return CreateClientWithPermissionsAsync(
            email,
            providerId,
            [Permissions.MirrorRead, Permissions.MirrorManage, Permissions.MirrorConfigure]);
    }

    private async Task SeedMirrorEntriesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IProviderMirrorRepository>();
        var modules = scope.ServiceProvider.GetRequiredService<IModuleMirrorRepository>();

        await providers.UpsertProviderPackageAsync(new MirrorProviderPackage
        {
            Hostname = "registry.example.com",
            Namespace = "hashicorp",
            Type = "aws",
            Version = "5.0.0",
            Os = "linux",
            Arch = "amd64",
            DownloadUrl = "https://registry.example.com/aws.zip",
            State = "failed",
            LastError = "timeout",
            LastSyncAt = DateTime.UtcNow.AddMinutes(-2)
        });

        await modules.UpsertModulePackageAsync(new MirrorModulePackage
        {
            Hostname = "registry.example.com",
            Namespace = "hashicorp",
            Name = "vpc",
            Provider = "aws",
            Version = "1.2.3",
            DownloadUrl = "https://registry.example.com/vpc.zip",
            State = "ready",
            LastSyncAt = DateTime.UtcNow.AddMinutes(-1)
        });
    }
}
