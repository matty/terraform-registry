using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ModuleTrashTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string TestDataDirectory = "TestData";
    private const string TestModuleName = "test-module.zip";
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task SoftDelete_ExistingModule_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.0.0");

        var response = await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.0.0");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SoftDelete_NonExistentModule_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/99.99.99");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restore_SoftDeletedModule_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.1.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.1.0");

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/2.1.0/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Purge_SoftDeletedModule_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.2.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.2.0");

        var response = await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.2.0/purge");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Purge_ThenListTrash_ModuleIsGone()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.3.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.3.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.3.0/purge");

        var trashResponse = await client.GetAsync("/v1/modules/trash");
        var content = await trashResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("2.3.0", content);
    }

    [Fact]
    public async Task SoftDeletedModule_NotVisibleInModuleList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.4.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.4.0");

        var listResponse = await client.GetAsync("/v1/modules");
        var content = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("2.4.0", content);
    }

    [Fact]
    public async Task SoftDeletedModule_DetailReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.6.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.6.0");

        var detailResponse = await client.GetAsync("/v1/modules/test-ns/test-name/test-provider/2.6.0");

        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Fact]
    public async Task SoftDeletedModule_VisibleInTrashList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "2.5.0");
        await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/2.5.0");

        var trashResponse = await client.GetAsync("/v1/modules/trash");
        var content = await trashResponse.Content.ReadAsStringAsync();
        Assert.Contains("2.5.0", content);
    }

    private async Task UploadTestModule(HttpClient client, string version)
    {
        var projectDir = GetProjectDirectory();
        var moduleFilePath = Path.Combine(projectDir, TestDataDirectory, TestModuleName);
        var fileName = Path.GetFileName(moduleFilePath);

        await using var fileStream = File.OpenRead(moduleFilePath);
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        content.Add(streamContent, "moduleFile", fileName);

        var uploadResponse = await client.PostAsync($"/v1/modules/test-ns/test-name/test-provider/{version}", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
    }

    private string GetProjectDirectory()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        var projectDir = Directory.GetParent(assemblyDirectory)?.Parent?.Parent?.FullName;

        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            throw new DirectoryNotFoundException("Could not locate the test project directory.");

        return projectDir;
    }
}
