using System.Net;
using System.Net.Http.Headers;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class HealthEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task Ready_ReturnsOkWithMinimalResponse()
    {
        var response = await _client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", body);
        Assert.DoesNotContain("database", body);
        Assert.DoesNotContain("storage", body);
        Assert.DoesNotContain("providerArtifactStorage", body);
    }

    [Fact]
    public async Task Ready_WithDetailAndAuth_ReturnsComponentBreakdown()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database", body);
        Assert.Contains("storage", body);
        Assert.Contains("providerArtifactStorage", body);
    }

    [Fact]
    public async Task Ready_WithDetailButNoAuth_ReturnsMinimalResponse()
    {
        var response = await _client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("database", body);
        Assert.DoesNotContain("storage", body);
        Assert.DoesNotContain("providerArtifactStorage", body);
    }
}
