using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TerraformRegistry.Tests.IntegrationTests;

public sealed class BrowserSecurityHeaderTests
{
    [Fact]
    public async Task ApiResponseIncludesBaselineBrowserSecurityHeaders()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AuthorizationToken"] = "browser-security-test-token",
                    ["Sqlite:ConnectionString"] = "Data Source=/tmp/terraform-registry-browser-security-tests.db"
                }));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/missing");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
    }
}
