using System.Net;
using System.Net.Http.Headers;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class HealthEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task HealthReturnsOk()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task ReadyReturnsOkWithMinimalResponse()
    {
        var response = await Client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", body);
        Assert.DoesNotContain("database", body);
        Assert.DoesNotContain("storage", body);
        Assert.DoesNotContain("providerArtifactStorage", body);
    }

    [Fact]
    public async Task ReadyWithDetailAndAuthReturnsComponentBreakdown()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database", body);
        Assert.Contains("storage", body);
        Assert.Contains("providerArtifactStorage", body);
    }

    [Fact]
    public async Task ReadyWithDetailButNoAuthReturnsMinimalResponse()
    {
        var response = await Client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("database", body);
        Assert.DoesNotContain("storage", body);
        Assert.DoesNotContain("providerArtifactStorage", body);
    }
}
