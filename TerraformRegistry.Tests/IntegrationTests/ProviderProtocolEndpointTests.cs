using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ProviderProtocolEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task ProviderVersions_ReturnsPublishedProviderVersions()
    {
        await SeedProviderReleaseAsync();
        var client = await CreateClientWithPermissionsAsync(
            "provider-reader@example.com",
            "provider-reader",
            [Permissions.ProvidersRead]);

        var response = await client.GetAsync("/v1/providers/acme/example/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var version = json.GetProperty("versions")[0];
        Assert.Equal("1.0.0", version.GetProperty("version").GetString());
        Assert.Equal("linux", version.GetProperty("platforms")[0].GetProperty("os").GetString());
        Assert.Equal("amd64", version.GetProperty("platforms")[0].GetProperty("arch").GetString());
    }

    [Fact]
    public async Task ProviderDownload_ReturnsTerraformPackageMetadata()
    {
        await SeedProviderReleaseAsync();
        var client = await CreateClientWithPermissionsAsync(
            "provider-download@example.com",
            "provider-download",
            [Permissions.ProvidersRead]);

        var response = await client.GetAsync("/v1/providers/acme/example/1.0.0/download/linux/amd64");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip", json.GetProperty("filename").GetString());
        Assert.Equal("linux", json.GetProperty("os").GetString());
        Assert.Equal("amd64", json.GetProperty("arch").GetString());
        Assert.Equal(ExpectedShasum, json.GetProperty("shasum").GetString());
        Assert.True(json.GetProperty("download_url").GetString()?.Length > 0);
        Assert.True(json.GetProperty("shasums_url").GetString()?.Length > 0);
        Assert.True(json.GetProperty("shasums_signature_url").GetString()?.Length > 0);
        Assert.Equal("test-key", json.GetProperty("signing_keys").GetProperty("gpg_public_keys")[0].GetProperty("key_id").GetString());
    }

    private const string ExpectedShasum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private async Task SeedProviderReleaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IProviderArtifactStorage>();

        var provider = await repository.CreateProviderAsync(new TerraformProvider
        {
            Namespace = "acme",
            Type = "example",
            DisplayName = "Example",
            Description = "Example provider",
            SourceRepositoryUrl = "https://example.com/acme/example",
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await repository.AddGpgKeyAsync(new ProviderGpgKey
        {
            Namespace = "acme",
            KeyId = "test-key",
            AsciiArmor = "-----BEGIN PGP PUBLIC KEY BLOCK-----\n\nmock\n-----END PGP PUBLIC KEY BLOCK-----",
            Source = "test",
            SourceUrl = "https://example.com/key.asc",
            CreatedAt = DateTime.UtcNow
        });

        var version = await repository.CreateProviderVersionAsync(provider.Id, "1.0.0", ["5.0"], "test-key");
        var platform = await repository.CreateProviderPlatformAsync(
            version.Id,
            "linux",
            "amd64",
            "terraform-provider-example_1.0.0_linux_amd64.zip",
            ExpectedShasum);

        await using var package = new MemoryStream([1, 2, 3]);
        var packageResult = await storage.SaveAsync(
            "acme/example/1.0.0/linux_amd64/terraform-provider-example_1.0.0_linux_amd64.zip",
            package,
            CancellationToken.None);
        await repository.SetPlatformPackagePathAsync(platform.Id, packageResult.StoragePath, packageResult.SizeBytes);

        await using var shasums = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = "terraform-provider-example_1.0.0_linux_amd64.zip",
            shasum = ExpectedShasum
        }));
        var shasumsResult = await storage.SaveAsync(
            "acme/example/1.0.0/terraform-provider-example_1.0.0_SHA256SUMS",
            shasums,
            CancellationToken.None);
        await repository.SetVersionShasumsPathAsync(version.Id, shasumsResult.StoragePath);

        await using var signature = new MemoryStream([4, 5, 6]);
        var signatureResult = await storage.SaveAsync(
            "acme/example/1.0.0/terraform-provider-example_1.0.0_SHA256SUMS.sig",
            signature,
            CancellationToken.None);
        await repository.SetVersionShasumsSignaturePathAsync(version.Id, signatureResult.StoragePath);
    }
}
