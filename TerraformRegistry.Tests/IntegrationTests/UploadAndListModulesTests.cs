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
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Output.WriteLine($"List modules status: {listResponse.StatusCode}");
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Output.WriteLine($"List modules response: {listContent}");

        using var json = JsonDocument.Parse(listContent);
        Assert.Equal("1", json.RootElement.GetProperty("meta").GetProperty("total").GetString());
    }

    [Fact]
    public async Task ListModulesOutputsResponse()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var listResponse = await client.GetAsync("/v1/modules?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal("{\"modules\":[],\"meta\":{\"limit\":\"10\",\"current_offset\":\"0\",\"total\":\"0\"}}", listContent);
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

    [Fact]
    public async Task ListModulesPagesCoordinatesAndKeepsVersionsForTheSelectedCoordinate()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        foreach (var (name, version) in new[]
                 {
                     ("alpha", "1.0.0"), ("alpha", "2.0.0"), ("bravo", "1.0.0"), ("charlie", "1.0.0")
                 })
        {
            using var content = CreateModuleUploadContent();
            var response = await client.PostAsync($"/v1/modules/test-ns/{name}/test-provider/{version}", content);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var responsePage = await client.GetAsync("/v1/modules?namespace=test-ns&provider=test-provider&offset=1&limit=1");
        Assert.Equal(HttpStatusCode.OK, responsePage.StatusCode);
        using var json = JsonDocument.Parse(await responsePage.Content.ReadAsStringAsync());
        var module = Assert.Single(json.RootElement.GetProperty("modules").EnumerateArray());

        Assert.Equal("bravo", module.GetProperty("name").GetString());
        Assert.Equal("3", json.RootElement.GetProperty("meta").GetProperty("total").GetString());
    }
}
