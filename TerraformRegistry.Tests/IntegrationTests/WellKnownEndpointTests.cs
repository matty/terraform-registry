using System.Net;
using System.Text.Json;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class WellKnownEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task WellKnownEndpointReturnsExpectedResponse()
    {
        Output.WriteLine("Sending request to /.well-known/terraform.json");
        var response = await Client.GetAsync("/.well-known/terraform.json");
        Output.WriteLine($"Received status code: {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(); Output.WriteLine($"Received content: {content}");

        // Assert the JSON content
        var expectedJson = new
        {
            modules_v1 = "/v1/modules/"
        };

        using var jsonDoc = JsonDocument.Parse(content);
        var actualJson = new
        {
            modules_v1 = jsonDoc.RootElement.GetProperty("modules.v1").GetString(),
            providers_v1 = jsonDoc.RootElement.GetProperty("providers.v1").GetString()
        };

        Assert.Equal(expectedJson.modules_v1, actualJson.modules_v1);
        Assert.Equal("/v1/providers/", actualJson.providers_v1);
    }

    [Fact]
    public async Task WellKnownEndpointExposesLoginV1OAuthMetadata()
    {
        var response = await Client.GetAsync("/.well-known/terraform.json");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal("/v1/modules/", root.GetProperty("modules.v1").GetString());
        Assert.Equal("/v1/providers/", root.GetProperty("providers.v1").GetString());

        var login = root.GetProperty("login.v1");
        Assert.Equal("terraform-cli", login.GetProperty("client").GetString());
        Assert.Equal("/api/auth/terraform/authorize", login.GetProperty("authz").GetString());
        Assert.Equal("/api/auth/terraform/token", login.GetProperty("token").GetString());
        Assert.Contains(
            login.GetProperty("grant_types").EnumerateArray().Select(x => x.GetString()),
            x => x == "authz_code");
    }
}
