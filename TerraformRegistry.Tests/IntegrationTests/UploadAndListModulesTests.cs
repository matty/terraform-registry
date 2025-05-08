using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class UploadAndListModulesTests(ITestOutputHelper output) : UploadModuleTests(output)
{
    [Fact]
    public async Task Upload_ValidModule_Then_ListModules_OutputsResponse()
    {
        // Call the existing upload test
        await Upload_ValidModule_ReturnsOk();

        // Now fetch all modules
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var listResponse = await client.GetAsync("/v1/modules?offset=0&limit=10");
        _output.WriteLine($"List modules status: {listResponse.StatusCode}");
        var listContent = await listResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"List modules response: {listContent}");
    }

    [Fact]
    public async Task ListModules_OutputsResponse()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var listResponse = await client.GetAsync("/v1/modules?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal("{\"modules\":[],\"meta\":{\"limit\":\"10\",\"current_offset\":\"0\"}}", listContent);

    }
}