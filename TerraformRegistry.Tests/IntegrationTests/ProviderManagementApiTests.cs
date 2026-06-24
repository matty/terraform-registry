using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ProviderManagementApiTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";
    private const string ValidShasum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task ProvidersUnauthenticatedReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListProvidersWithoutSearchQueryReturnsProviders()
    {
        var client = await CreateClientWithPermissionsAsync(
            "provider-list@example.com",
            "provider-list",
            [Permissions.ProvidersPublish, Permissions.ProvidersRead]);
        var ns = NewNamespace();
        await CreateProviderAsync(client, ns);

        var response = await client.GetAsync("/api/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var provider = Assert.Single(json.GetProperty("providers").EnumerateArray());
        Assert.Equal(ns, provider.GetProperty("namespace").GetString());
        Assert.Equal("example", provider.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateProviderWithPublishPermissionReturnsCreatedAndCanBeRead()
    {
        var client = await CreateClientWithPermissionsAsync(
            "provider-create@example.com",
            "provider-create",
            [Permissions.ProvidersPublish, Permissions.ProvidersRead]);
        var ns = NewNamespace();

        var response = await client.PostAsJsonAsync("/api/providers", new CreateProviderRequest
        {
            Namespace = ns,
            Type = "example",
            DisplayName = "Example",
            Description = "Example provider"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/providers/{ns}/example");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ns, json.GetProperty("namespace").GetString());
        Assert.Equal("example", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task CreateProviderDuplicateReturnsConflict()
    {
        var client = await CreateClientWithPermissionsAsync(
            "provider-duplicate@example.com",
            "provider-duplicate",
            [Permissions.ProvidersPublish, Permissions.ProvidersRead]);
        var ns = NewNamespace();
        var request = new CreateProviderRequest { Namespace = ns, Type = "example" };

        var first = await client.PostAsJsonAsync("/api/providers", request);
        var second = await client.PostAsJsonAsync("/api/providers", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateVersionWithValidSemVerReturnsCreated()
    {
        var client = await CreatePublisherClientAsync("provider-version@example.com", "provider-version");
        var ns = NewNamespace();
        await CreateProviderAndGpgKeyAsync(client, ns);

        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions", new CreateProviderVersionRequest
        {
            Version = "1.0.0",
            Protocols = ["5.0"],
            KeyId = "test-key"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1.0.0", json.GetProperty("version").GetString());
    }

    [Fact]
    public async Task CreateVersionWithDocumentedProtocolMinorVersionReturnsCreated()
    {
        var client = await CreatePublisherClientAsync("provider-version-52@example.com", "provider-version-52");
        var ns = NewNamespace();
        await CreateProviderAndGpgKeyAsync(client, ns);

        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions", new CreateProviderVersionRequest
        {
            Version = "1.0.0",
            Protocols = ["5.2"],
            KeyId = "test-key"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("5.2", json.GetProperty("protocols")[0].GetString());
    }

    [Fact]
    public async Task CreateVersionWithUnsupportedProtocolReturnsBadRequest()
    {
        var client = await CreatePublisherClientAsync("provider-bad-protocol@example.com", "provider-bad-protocol");
        var ns = NewNamespace();
        await CreateProviderAndGpgKeyAsync(client, ns);

        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions", new CreateProviderVersionRequest
        {
            Version = "1.0.0",
            Protocols = ["4.0"],
            KeyId = "test-key"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlatformWithValidMetadataReturnsCreatedAndCanBeListed()
    {
        var client = await CreatePublisherClientAsync("provider-platform@example.com", "provider-platform");
        var ns = NewNamespace();
        await CreateProviderVersionAsync(client, ns);

        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions/1.0.0/platforms",
            new CreateProviderPlatformRequest
            {
                Os = "linux",
                Arch = "amd64",
                Filename = "terraform-provider-example_1.0.0_linux_amd64.zip",
                Shasum = ValidShasum
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listResponse = await client.GetAsync($"/api/providers/{ns}/example/versions/1.0.0/platforms");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var json = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("linux", json.GetProperty("platforms")[0].GetProperty("os").GetString());
    }

    [Fact]
    public async Task ListVersionsAndPlatformsReturnManagementMetadata()
    {
        var client = await CreatePublisherClientAsync("provider-management-metadata@example.com", "provider-management-metadata");
        var ns = NewNamespace();
        await CreateProviderVersionAsync(client, ns);
        await UploadShasumsAndSignatureAsync(client, ns);
        await CreatePlatformAsync(client, ns);
        await MarkPlatformPackageUploadedAsync(ns);

        var versionsResponse = await client.GetAsync($"/api/providers/{ns}/example/versions");

        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versionsJson = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var version = versionsJson.GetProperty("versions")[0];
        Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("id").GetString()));
        Assert.Equal("1.0.0", version.GetProperty("version").GetString());
        Assert.Equal("5.0", version.GetProperty("protocols")[0].GetString());
        Assert.Equal("test-key", version.GetProperty("key_id").GetString());
        Assert.True(version.GetProperty("has_shasums").GetBoolean());
        Assert.True(version.GetProperty("has_shasums_signature").GetBoolean());
        Assert.NotEqual(default, version.GetProperty("published_at").GetDateTime());

        var versionPlatform = version.GetProperty("platforms")[0];
        Assert.False(string.IsNullOrWhiteSpace(versionPlatform.GetProperty("id").GetString()));
        Assert.Equal("linux", versionPlatform.GetProperty("os").GetString());
        Assert.Equal("amd64", versionPlatform.GetProperty("arch").GetString());
        Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip", versionPlatform.GetProperty("filename").GetString());
        Assert.Equal(ValidShasum, versionPlatform.GetProperty("shasum").GetString());
        Assert.True(versionPlatform.GetProperty("has_package").GetBoolean());
        Assert.Equal(123, versionPlatform.GetProperty("size_bytes").GetInt64());
        Assert.NotEqual(default, versionPlatform.GetProperty("uploaded_at").GetDateTime());

        var platformsResponse = await client.GetAsync($"/api/providers/{ns}/example/versions/1.0.0/platforms");

        Assert.Equal(HttpStatusCode.OK, platformsResponse.StatusCode);
        var platformsJson = await platformsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var platform = platformsJson.GetProperty("platforms")[0];
        Assert.False(string.IsNullOrWhiteSpace(platform.GetProperty("id").GetString()));
        Assert.Equal("linux", platform.GetProperty("os").GetString());
        Assert.Equal("amd64", platform.GetProperty("arch").GetString());
        Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip", platform.GetProperty("filename").GetString());
        Assert.Equal(ValidShasum, platform.GetProperty("shasum").GetString());
        Assert.True(platform.GetProperty("has_package").GetBoolean());
        Assert.Equal(123, platform.GetProperty("size_bytes").GetInt64());
        Assert.NotEqual(default, platform.GetProperty("uploaded_at").GetDateTime());
    }

    [Fact]
    public async Task ProtocolVersionsHidesReleaseUntilShasumsSignatureAndPackageAreUploaded()
    {
        var client = await CreatePublisherClientAsync("provider-protocol-gating@example.com", "provider-protocol-gating");
        var ns = NewNamespace();
        await CreateProviderVersionAsync(client, ns);
        await CreatePlatformAsync(client, ns);

        var beforeShasums = await client.GetAsync($"/v1/providers/{ns}/example/versions");
        Assert.Equal(HttpStatusCode.NotFound, beforeShasums.StatusCode);

        await UploadShasumsAndSignatureAsync(client, ns);
        var beforePackage = await client.GetAsync($"/v1/providers/{ns}/example/versions");
        Assert.Equal(HttpStatusCode.NotFound, beforePackage.StatusCode);

        await MarkPlatformPackageUploadedAsync(ns);
        var complete = await client.GetAsync($"/v1/providers/{ns}/example/versions");

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var json = await complete.Content.ReadFromJsonAsync<JsonElement>();
        var version = Assert.Single(json.GetProperty("versions").EnumerateArray());
        Assert.Equal("1.0.0", version.GetProperty("version").GetString());
        var platform = Assert.Single(version.GetProperty("platforms").EnumerateArray());
        Assert.Equal("linux", platform.GetProperty("os").GetString());
        Assert.Equal("amd64", platform.GetProperty("arch").GetString());
    }

    [Fact]
    public async Task AddGpgKeyWithoutKeysPermissionReturnsForbidden()
    {
        var client = await CreateClientWithPermissionsAsync(
            "provider-no-key@example.com",
            "provider-no-key",
            [Permissions.ProvidersPublish, Permissions.ProvidersRead]);
        var ns = NewNamespace();
        await CreateProviderAsync(client, ns);

        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/gpg-keys", NewGpgKeyRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeGpgKeyUsedByActiveVersionReturnsConflict()
    {
        var client = await CreatePublisherClientAsync("provider-revoke-key@example.com", "provider-revoke-key");
        var ns = NewNamespace();
        await CreateProviderVersionAsync(client, ns);

        var response = await client.DeleteAsync($"/api/providers/{ns}/example/gpg-keys/test-key");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UploadShasumsAndSignatureWithPublishPermissionReturnsNoContent()
    {
        var client = await CreatePublisherClientAsync("provider-files@example.com", "provider-files");
        var ns = NewNamespace();
        await CreateProviderVersionAsync(client, ns);

        using var shasums = new StringContent(
            $"{ValidShasum}  terraform-provider-example_1.0.0_linux_amd64.zip\n",
            Encoding.UTF8,
            "text/plain");
        var shasumsResponse = await client.PutAsync($"/api/providers/{ns}/example/versions/1.0.0/shasums", shasums);

        using var signature = new ByteArrayContent([1, 2, 3]);
        var signatureResponse = await client.PutAsync($"/api/providers/{ns}/example/versions/1.0.0/shasums.sig", signature);

        Assert.Equal(HttpStatusCode.NoContent, shasumsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signatureResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteProviderEnforcesDedicatedPermissions()
    {
        var creator = await CreatePublisherClientAsync("provider-lifecycle-create@example.com", "provider-lifecycle-create");
        var ns = NewNamespace();
        await CreateProviderAsync(creator, ns);

        var editor = await CreateClientWithPermissionsAsync(
            "provider-lifecycle-edit@example.com",
            "provider-lifecycle-edit",
            [Permissions.ProvidersRead, Permissions.ProvidersDescription, Permissions.ProvidersDelete]);
        var updateResponse = await editor.PatchAsJsonAsync($"/api/providers/{ns}/example", new UpdateProviderRequest
        {
            Description = "Updated provider"
        });
        var deleteResponse = await editor.DeleteAsync($"/api/providers/{ns}/example");
        var getAfterDelete = await editor.GetAsync($"/api/providers/{ns}/example");

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    private async Task<HttpClient> CreatePublisherClientAsync(string email, string providerId)
    {
        return await CreateClientWithPermissionsAsync(email, providerId,
            [Permissions.ProvidersRead, Permissions.ProvidersPublish, Permissions.ProvidersKeysManage]);
    }

    private static async Task CreateProviderVersionAsync(HttpClient client, string ns)
    {
        await CreateProviderAndGpgKeyAsync(client, ns);
        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions", new CreateProviderVersionRequest
        {
            Version = "1.0.0",
            Protocols = ["5.0"],
            KeyId = "test-key"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task UploadShasumsAndSignatureAsync(HttpClient client, string ns)
    {
        using var shasums = new StringContent(
            $"{ValidShasum}  terraform-provider-example_1.0.0_linux_amd64.zip\n",
            Encoding.UTF8,
            "text/plain");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/providers/{ns}/example/versions/1.0.0/shasums", shasums)).StatusCode);

        using var signature = new ByteArrayContent([1, 2, 3]);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/providers/{ns}/example/versions/1.0.0/shasums.sig", signature)).StatusCode);
    }

    private static async Task CreatePlatformAsync(HttpClient client, string ns)
    {
        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/versions/1.0.0/platforms",
            new CreateProviderPlatformRequest
            {
                Os = "linux",
                Arch = "amd64",
                Filename = "terraform-provider-example_1.0.0_linux_amd64.zip",
                Shasum = ValidShasum
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task MarkPlatformPackageUploadedAsync(string ns)
    {
        using var scope = Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();
        var platform = await repository.GetProviderPlatformAsync(ns, "example", "1.0.0", "linux", "amd64");
        Assert.NotNull(platform);
        Assert.True(await repository.SetPlatformPackagePathAsync(platform!.Id, "test/package.zip", 123));
    }

    private static async Task CreateProviderAndGpgKeyAsync(HttpClient client, string ns)
    {
        await CreateProviderAsync(client, ns);
        var response = await client.PostAsJsonAsync($"/api/providers/{ns}/example/gpg-keys", NewGpgKeyRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task CreateProviderAsync(HttpClient client, string ns)
    {
        var response = await client.PostAsJsonAsync("/api/providers", new CreateProviderRequest
        {
            Namespace = ns,
            Type = "example",
            DisplayName = "Example"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static CreateProviderGpgKeyRequest NewGpgKeyRequest() => new()
    {
        KeyId = "test-key",
        AsciiArmor = "-----BEGIN PGP PUBLIC KEY BLOCK-----\n\nmock\n-----END PGP PUBLIC KEY BLOCK-----",
        Source = "test"
    };

    private static string NewNamespace() => $"acme{Guid.NewGuid():N}";
}
