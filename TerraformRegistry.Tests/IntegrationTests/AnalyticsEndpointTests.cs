using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using TerraformRegistry.API;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class AnalyticsEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string TestDataDirectory = "TestData";
    private const string TestModuleName = "test-module.zip";
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Analytics_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        // No auth header

        var response = await client.GetAsync("/api/analytics/downloads/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Analytics_Summary_ReturnsValidJson()
    {
        var client = await CreateAnalyticsClientAsync("analytics-summary@example.com", "analytics-summary-id");

        var response = await client.GetAsync("/api/analytics/downloads/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("totalDownloads", out _));
        Assert.True(json.TryGetProperty("downloadsToday", out _));
        Assert.True(json.TryGetProperty("downloadsThisWeek", out _));
        Assert.True(json.TryGetProperty("downloadsThisMonth", out _));
        Assert.True(json.TryGetProperty("uniqueModules", out _));
    }

    [Fact]
    public async Task Analytics_TopModules_ReturnsValidJson()
    {
        var client = await CreateAnalyticsClientAsync("analytics-top@example.com", "analytics-top-id");

        var response = await client.GetAsync("/api/analytics/downloads/top?limit=5&period=30d");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("period", out _));
        Assert.True(json.TryGetProperty("modules", out _));
    }

    [Fact]
    public async Task Analytics_Trends_ReturnsValidJson()
    {
        var client = await CreateAnalyticsClientAsync("analytics-trends@example.com", "analytics-trends-id");

        var response = await client.GetAsync("/api/analytics/downloads/trends?period=30d&interval=day");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("period", out _));
        Assert.True(json.TryGetProperty("interval", out _));
        Assert.True(json.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task Analytics_AfterDownload_ReflectsInSummary()
    {
        var client = await CreateAnalyticsClientAsync("analytics-download@example.com", "analytics-download-id",
            Permissions.ModulesRead, Permissions.ModulesUpload);

        // Upload a module
        await UploadTestModule(client, "1.0.0");

        // Download the module — the test HttpClient follows redirects automatically,
        // so a 302 -> /module/download?token=... becomes 200 with file content.
        var downloadResponse = await client.GetAsync(
            "/v1/modules/test-ns/test-name/test-provider/1.0.0/download");
        Assert.True(
            downloadResponse.IsSuccessStatusCode || downloadResponse.StatusCode == HttpStatusCode.NoContent,
            $"Expected success status but got {downloadResponse.StatusCode}");

        // Wait for fire-and-forget recording to complete
        await Task.Delay(1000);

        // Check analytics summary
        var summaryResponse = await client.GetAsync("/api/analytics/downloads/summary");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);

        var json = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var totalDownloads = json.GetProperty("totalDownloads").GetInt64();
        Assert.True(totalDownloads > 0, $"Expected totalDownloads > 0 but got {totalDownloads}");
    }

    private async Task UploadTestModule(HttpClient client, string version)
    {
        var projectDir = GetProjectDirectory();
        var moduleFilePath = Path.Combine(projectDir, TestDataDirectory, TestModuleName);
        var fileName = Path.GetFileName(moduleFilePath);

        await using var fileStream = File.OpenRead(moduleFilePath);
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        content.Add(streamContent, "moduleFile", fileName);

        var uploadResponse = await client.PostAsync($"/v1/modules/test-ns/test-name/test-provider/{version}", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
    }

    private Task<HttpClient> CreateAnalyticsClientAsync(string email, string providerId, params string[] extraPermissions)
    {
        var permissions = new[] { Permissions.AnalyticsView }.Concat(extraPermissions).ToArray();
        return CreateClientWithPermissionsAsync(email, providerId, permissions);
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
