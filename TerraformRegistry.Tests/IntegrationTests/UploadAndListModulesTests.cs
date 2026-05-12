using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class UploadAndListModulesTests(ITestOutputHelper output) : UploadModuleTests(output)
{
    [Fact]
    public async Task UploadValidModuleThenListModulesOutputsResponse()
    {
        // Call the existing upload test
        await UploadValidModuleReturnsOk();

        // Now fetch all modules
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var listResponse = await client.GetAsync("/v1/modules?offset=0&limit=10");
        Output.WriteLine($"List modules status: {listResponse.StatusCode}");
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"List modules response: {listContent}");
    }

    [Fact]
    public async Task ListModulesOutputsResponse()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var listResponse = await client.GetAsync("/v1/modules?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal("{\"modules\":[],\"meta\":{\"limit\":\"10\",\"current_offset\":\"0\"}}", listContent);
    }

    [Fact]
    public async Task ListModulesUsesSemVerPrecedenceForLatestVersion()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var olderContent = CreateModuleUploadContent();
        var olderResponse = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/1.9.0", olderContent);
        Assert.Equal(HttpStatusCode.Created, olderResponse.StatusCode);

        using var newerContent = CreateModuleUploadContent();
        var newerResponse = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/1.10.0", newerContent);
        Assert.Equal(HttpStatusCode.Created, newerResponse.StatusCode);

        var listResponse = await client.GetAsync("/v1/modules?namespace=test-ns&provider=test-provider&offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(listContent);
        var modules = json.RootElement.GetProperty("modules");
        var module = Assert.Single(modules.EnumerateArray());

        Assert.Equal("1.10.0", module.GetProperty("version").GetString());
    }
}
