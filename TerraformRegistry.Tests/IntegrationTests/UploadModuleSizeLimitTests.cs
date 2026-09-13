using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

/// <summary>
/// An archive over the configured limit must be refused with 413 and the registry's own
/// error body, not a bare transport rejection and not a 400.
/// </summary>
public class UploadModuleSizeLimitTests(ITestOutputHelper output) : UploadModuleTests(output)
{
    private const long TinyArchiveLimitBytes = 128;

    protected override void ConfigureTestApp(IWebHostBuilder builder)
    {
        base.ConfigureTestApp(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleExtraction:MaxArchiveBytes"] = TinyArchiveLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        });
    }

    [Fact]
    public async Task UploadOverTheConfiguredArchiveLimitReturnsPayloadTooLarge()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var content = CreateModuleUploadContent();

        var response = await client.PostAsync("/v1/modules/limit-ns/limit-name/limit-provider/0.1.0", content);
        var body = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"Response {(int)response.StatusCode}: {body}");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        // The registry's own error contract, proving the handler rejected it rather than the
        // transport cutting the request off.
        Assert.Contains("exceeds the configured limit", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TinyArchiveLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), body,
            StringComparison.Ordinal);
    }
}
