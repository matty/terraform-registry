using System.Net;
using System.Net.Http.Json;
using TerraformRegistry.API;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ApiKeySharingAuthorizationTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task UserWithoutSharedPermissionCannotListSharedKeys()
    {
        var client = await CreateClientWithPermissionsAsync("shared-list-denied@test.com",
            "shared-list-denied-id",
            [Permissions.ApiKeysManage]);

        var response = await client.GetAsync("/api/keys/shared");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutSharedPermissionCannotCreateSharedKey()
    {
        var client = await CreateClientWithPermissionsAsync("shared-create-denied@test.com",
            "shared-create-denied-id",
            [Permissions.ApiKeysManage]);

        var response = await client.PostAsJsonAsync("/api/keys", new
        {
            description = "shared key",
            isShared = true
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutSharedPermissionCanCreatePersonalKey()
    {
        var client = await CreateClientWithPermissionsAsync("personal-create@test.com",
            "personal-create-id",
            [Permissions.ApiKeysManage]);

        var response = await client.PostAsJsonAsync("/api/keys", new
        {
            description = "personal key",
            isShared = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserWithSharedPermissionCanCreateAndListSharedKeys()
    {
        var client = await CreateClientWithPermissionsAsync(
            "shared-create-allowed@test.com",
            "shared-create-allowed-id",
            [Permissions.ApiKeysManage, Permissions.ApiKeysShared]);

        var createResponse = await client.PostAsJsonAsync("/api/keys", new
        {
            description = "shared key",
            isShared = true
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/keys/shared");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("shared key", body, StringComparison.Ordinal);
    }
}
