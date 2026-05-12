using System.Net;
using TerraformRegistry.API;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ProviderProtocolAuthorizationTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task ProviderVersionsWithoutAuthorizationReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/v1/providers/acme/example/versions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProviderVersionsUserWithoutProvidersReadReturnsForbidden()
    {
        var client = await CreateClientWithPermissionsAsync(
            "no-provider-read@example.com",
            "no-provider-read",
            [Permissions.ModulesRead]);

        var response = await client.GetAsync("/v1/providers/acme/example/versions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
