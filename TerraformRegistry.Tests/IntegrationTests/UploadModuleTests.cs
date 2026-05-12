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
    public async Task InvalidAuthorizationReturnsUnauthorized()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.1.0", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadValidModuleReturnsOk()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var content = CreateModuleUploadContent();

        var response = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.1.0", content);

        var responseContent = await response.Content.ReadAsStringAsync();
        Output.WriteLine($"Response content: {responseContent}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task UploadDuplicateModuleWithoutReplaceReturnsConflict()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var firstContent = CreateModuleUploadContent();
        var firstResponse = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.2.0", firstContent);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateContent = CreateModuleUploadContent();
        var duplicateResponse = await client.PostAsync("/v1/modules/test-ns/test-name/test-provider/0.2.0", duplicateContent);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task UploadInvalidModuleCoordinateReturnsBadRequest()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "moduleFile", "module.zip");

        var response = await client.PostAsync("/v1/modules/bad.namespace/test-name/test-provider/1.0.0", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     Gets the test project directory path
    /// </summary>
    private static string GetProjectDirectory()
    {
        // Find the project directory by starting from the current assembly location and going up
        // until we find the directory containing the test project file
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? throw new DirectoryNotFoundException("Could not locate the test assembly directory.");

        // Navigate to the project directory (going up from bin/Debug/net9.0)
        var projectDir = Directory.GetParent(assemblyDirectory)?.Parent?.Parent?.FullName;

        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            throw new DirectoryNotFoundException("Could not locate the test project directory.");

        return projectDir;
    }

    protected MultipartFormDataContent CreateModuleUploadContent()
    {
        var projectDir = GetProjectDirectory();
        var moduleFilePath = Path.Combine(projectDir, TestDataDirectory, TestModuleName);
        var fileName = Path.GetFileName(moduleFilePath);

        Output.WriteLine($"Looking for test module at: {moduleFilePath}");

        if (!File.Exists(moduleFilePath))
        {
            Output.WriteLine(
                $"Test module file not found. Ensure '{TestModuleName}' exists in the '{TestDataDirectory}' folder at the root of the test project.");
            throw new FileNotFoundException("Test module file missing.", moduleFilePath);
        }

        var fileStream = File.OpenRead(moduleFilePath);
        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        content.Add(streamContent, "moduleFile", fileName);
        return content;
    }
}
