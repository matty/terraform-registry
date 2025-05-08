using System.Net;
using System.Text.Json;
using TerraformRegistry.Tests.IntegrationTests;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class WellKnownEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task WellKnown_Endpoint_Returns_Expected_Response()
    {
        _output.WriteLine("Sending request to /.well-known/terraform.json");
        var response = await _client.GetAsync("/.well-known/terraform.json");
        _output.WriteLine($"Received status code: {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Received content: {content}");

        // Assert the JSON content
        var expectedJson = new
        {
            modules = new
            {
                service_discovery = "/.well-known/terraform.json",
                modules = "/v1/modules/"
            }
        };

        using var jsonDoc = JsonDocument.Parse(content);
        var actualJson = new
        {
            modules = new
            {
                service_discovery = jsonDoc.RootElement.GetProperty("modules").GetProperty("service-discovery").GetString(),
                modules = jsonDoc.RootElement.GetProperty("modules").GetProperty("modules").GetString()
            }
        };

        Assert.Equal(expectedJson.modules.service_discovery, actualJson.modules.service_discovery);
        Assert.Equal(expectedJson.modules.modules, actualJson.modules.modules);
    }
}