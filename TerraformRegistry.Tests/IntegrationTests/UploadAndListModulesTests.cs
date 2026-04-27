using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;
using System.Text;
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
        Assert.Equal("{\"modules\":[],\"meta\":{\"limit\":\"10\",\"current_offset\":\"0\",\"total_count\":\"0\",\"has_more\":\"false\",\"next_offset\":\"0\"}}", listContent);
    }

    [Fact]
    public async Task ListModules_UsesSemVerPrecedenceForLatestVersion()
    {
        var client = _factory.CreateClient();
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
    public async Task ListModules_FiltersByRequiredProvider_AndReturnsPagingMetadata()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var awsContent = CreateModuleUploadContent(fileBytes: CreateModuleArchiveWithManifest(
            "{\"description\":\"AWS module\",\"providers\":{\"aws\":\"~> 5.0\",\"random\":\">= 3.0\"}}"));
        var awsResponse = await client.PostAsync("/v1/modules/team-a/network/aws/1.0.0", awsContent);
        Assert.Equal(HttpStatusCode.Created, awsResponse.StatusCode);

        using var azureContent = CreateModuleUploadContent(fileBytes: CreateModuleArchiveWithManifest(
            "{\"description\":\"Azure module\",\"providers\":{\"azurerm\":\"~> 4.0\"}}"));
        var azureResponse = await client.PostAsync("/v1/modules/team-a/identity/azurerm/1.0.0", azureContent);
        Assert.Equal(HttpStatusCode.Created, azureResponse.StatusCode);

        using var kubernetesContent = CreateModuleUploadContent(fileBytes: CreateModuleArchiveWithManifest(
            "{\"description\":\"Platform module\",\"providers\":{\"aws\":\"~> 5.0\",\"kubernetes\":\">= 2.0\"}}"));
        var kubernetesResponse = await client.PostAsync("/v1/modules/team-a/platform/aws/1.0.0", kubernetesContent);
        Assert.Equal(HttpStatusCode.Created, kubernetesResponse.StatusCode);

        var filteredResponse = await client.GetAsync("/v1/modules?required_provider=aws&offset=0&limit=1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);

        var listContent = await filteredResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(listContent);
        var modules = json.RootElement.GetProperty("modules");
        Assert.Single(modules.EnumerateArray());

        var meta = json.RootElement.GetProperty("meta");
        Assert.Equal("1", meta.GetProperty("limit").GetString());
        Assert.Equal("0", meta.GetProperty("current_offset").GetString());
        Assert.Equal("2", meta.GetProperty("total_count").GetString());
        Assert.Equal("true", meta.GetProperty("has_more").GetString());
        Assert.Equal("1", meta.GetProperty("next_offset").GetString());
    }

    private static byte[] CreateModuleArchiveWithManifest(string manifestJson)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mainTf = archive.CreateEntry("main.tf");
            using (var writer = new StreamWriter(mainTf.Open(), Encoding.UTF8))
            {
                writer.WriteLine("terraform {}");
            }

            var manifest = archive.CreateEntry("module.json");
            using var manifestWriter = new StreamWriter(manifest.Open(), Encoding.UTF8);
            manifestWriter.Write(manifestJson);
        }

        return memory.ToArray();
    }
}
