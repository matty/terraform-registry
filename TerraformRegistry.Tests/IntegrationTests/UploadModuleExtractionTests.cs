using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class UploadModuleExtractionTests(ITestOutputHelper output) : UploadModuleTests(output)
{
    protected override void ConfigureTestApp(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleExtraction:Enabled"] = "true",
                ["ModuleExtraction:ToolPath"] = "terraform-config-inspect-missing-for-test",
                ["ModuleExtraction:StartupBackfillBatchSize"] = "0"
            });
        });
    }

    [Fact]
    public async Task UploadModule_QueuesExtractionWithoutFailingThePublish()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var content = CreateModuleUploadContent();

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.9.0", content);

        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response content: {responseContent}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
