using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API;
using TerraformRegistry.Models;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ProviderManagementApiTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";
    private const string ValidShasum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Providers_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateProvider_WithPublishPermission_ReturnsCreatedAndCanBeRead()
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
    public async Task CreateProvider_Duplicate_ReturnsConflict()
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
    public async Task CreateVersion_WithValidSemVer_ReturnsCreated()
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
    public async Task CreateVersion_WithUnsupportedProtocol_ReturnsBadRequest()
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
    public async Task CreatePlatform_WithValidMetadata_ReturnsCreatedAndCanBeListed()
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
    public async Task AddGpgKey_WithoutKeysPermission_ReturnsForbidden()
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
    public async Task UploadShasumsAndSignature_WithPublishPermission_ReturnsNoContent()
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
    public async Task UpdateAndDeleteProvider_EnforcesDedicatedPermissions()
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

    private async Task CreateProviderVersionAsync(HttpClient client, string ns)
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
