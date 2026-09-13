using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

/// <summary>
/// An archive over the configured limit must be refused with 413 and the registry's own
/// error body, not a bare transport rejection and not a 400.
/// </summary>
public class UploadModuleSizeLimitTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";
    private const long TinyArchiveLimitBytes = 128;

    protected override void ConfigureTestApp(IWebHostBuilder builder)
    {
        base.ConfigureTestApp(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleExtraction:MaxArchiveBytes"] =
                    TinyArchiveLimitBytes.ToString(CultureInfo.InvariantCulture)
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
        Assert.Contains(TinyArchiveLimitBytes.ToString(CultureInfo.InvariantCulture), body, StringComparison.Ordinal);
    }

    private static MultipartFormDataContent CreateModuleUploadContent()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new DirectoryNotFoundException("Could not locate the test assembly directory.");
        var projectDir = Directory.GetParent(assemblyDirectory)?.Parent?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the test project directory.");

        var moduleFilePath = Path.Combine(projectDir, "TestData", "test-module.zip");
        if (!File.Exists(moduleFilePath))
            throw new FileNotFoundException("Test module file missing.", moduleFilePath);

        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(File.OpenRead(moduleFilePath));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        content.Add(streamContent, "moduleFile", Path.GetFileName(moduleFilePath));
        return content;
    }
}
