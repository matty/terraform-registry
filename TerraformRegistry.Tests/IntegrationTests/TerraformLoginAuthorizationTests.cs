using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class TerraformLoginAuthorizationTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task TerraformAuthorize_WithoutPortalSession_Redirects_To_Login_WithContinuation()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(
            "/api/auth/terraform/authorize?client_id=terraform-cli&redirect_uri=http://127.0.0.1:10000/&response_type=code&state=abc&code_challenge=xyz&code_challenge_method=S256");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/login?", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("returnTo=", response.Headers.Location.OriginalString, StringComparison.Ordinal);
    }
}
