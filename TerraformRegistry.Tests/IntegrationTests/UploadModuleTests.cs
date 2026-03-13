using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class UploadModuleTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string TestDataDirectory = "TestData";
    private const string TestModuleName = "test-module.zip";
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Invalid_Authorization_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.1.0", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidModule_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        // Get the project directory instead of the output directory
        var projectDir = GetProjectDirectory();
        var moduleFilePath = Path.Combine(projectDir, TestDataDirectory, TestModuleName);
        var fileName = Path.GetFileName(moduleFilePath);

        _output.WriteLine($"Looking for test module at: {moduleFilePath}");

        if (!File.Exists(moduleFilePath))
        {
            _output.WriteLine(
                $"Test module file not found. Ensure '{TestModuleName}' exists in the '{TestDataDirectory}' folder at the root of the test project.");
            throw new FileNotFoundException("Test module file missing.", moduleFilePath);
        }

        await using var fileStream = File.OpenRead(moduleFilePath);
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        content.Add(streamContent, "moduleFile", fileName);

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.1.0", content);

        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response content: {responseContent}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingModule_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        await UploadTestModule(client, "1.1.0");

        var deleteResponse = await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/1.1.0");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingModule_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var deleteResponse = await client.DeleteAsync("/v1/modules/test-ns/test-name/test-provider/9.9.9");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
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

    /// <summary>
    ///     Gets the test project directory path
    /// </summary>
    private string GetProjectDirectory()
    {
        // Find the project directory by starting from the current assembly location and going up
        // until we find the directory containing the test project file
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);

        // Navigate to the project directory (going up from bin/Debug/net9.0)
        var projectDir = Directory.GetParent(assemblyDirectory)?.Parent?.Parent?.FullName;

        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            throw new DirectoryNotFoundException("Could not locate the test project directory.");

        return projectDir;
    }
}
