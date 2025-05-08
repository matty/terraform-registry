using System.Net;
using System.Net.Http.Headers;
using Xunit;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class GetModuleUnauthorizedTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task GetModule_InvalidAuthorization_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.GetAsync("/v1/modules/test-ns/test-name/test-provider/0.1.0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
