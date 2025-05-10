using System.Net;
using System.Net.Http.Headers;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ListModulesUnauthorizedTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task ListModules_InvalidAuthorization_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}