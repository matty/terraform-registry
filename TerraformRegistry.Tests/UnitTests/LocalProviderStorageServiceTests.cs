using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class LocalProviderStorageServiceTests
{
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<LocalProviderStorageService>> _mockLogger;
    private readonly string _testStoragePath;

    public LocalProviderStorageServiceTests()
    {
        _mockLogger = new Mock<ILogger<LocalProviderStorageService>>();
        var inMemorySettings = new Dictionary<string, string>
        {
            { "ModuleStoragePath", Path.Combine(Path.GetTempPath(), "providers_test") },
            { "BaseUrl", "http://test.com" }
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _testStoragePath = _configuration["ModuleStoragePath"];

        // Clean up before test
        if (Directory.Exists(_testStoragePath)) Directory.Delete(_testStoragePath, true);
    }

    [Fact]
    public void Constructor_CreatesProviderStorageDirectory()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);
        Assert.True(Directory.Exists(Path.Combine(_testStoragePath, "providers")));
    }

    [Fact]
    public async Task UploadProviderAsync_WritesFileToDisk_AndReturnsRelativePath()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);
        var content = new MemoryStream(Encoding.UTF8.GetBytes("binary content"));

        var result = await service.UploadProviderAsync("ns", "type", "1.0.0", "linux", "amd64", content);

        var expectedRelativePath = Path.Combine("providers", "ns", "type", "1.0.0", "type_1.0.0_linux_amd64.zip");
        var fullPath = Path.Combine(_testStoragePath, expectedRelativePath);

        Assert.Equal(expectedRelativePath, result);
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task UploadShasumsAsync_WritesFileToDisk()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);
        var content = new MemoryStream(Encoding.UTF8.GetBytes("checksums"));

        await service.UploadShasumsAsync("ns", "type", "1.0.0", content);

        var fullPath = Path.Combine(_testStoragePath, "providers", "ns", "type", "1.0.0", "SHA256SUMS");
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task GetProviderDownloadUrlAsync_ReturnsCorrectApiEndpoint()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);
        var url = await service.GetProviderDownloadUrlAsync("ns", "type", "1.0.0", "linux", "amd64");

        Assert.Equal("http://test.com/v1/providers/ns/type/1.0.0/download/linux/amd64/file", url);
    }

    [Fact]
    public async Task GetFileStreamAsync_ReturnsStream_IfFileExists()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);

        // Setup file
        var content = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        await service.UploadProviderAsync("ns", "type", "1.0.0", "linux", "amd64", content);

        var relativePath = Path.Combine("providers", "ns", "type", "1.0.0", "type_1.0.0_linux_amd64.zip");
        var stream = await service.GetFileStreamAsync(relativePath);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        Assert.Equal("content", text);
    }

    [Fact]
    public async Task GetFileStreamAsync_ReturnsNull_IfFileDoesNotExist()
    {
        var service = new LocalProviderStorageService(_configuration, _mockLogger.Object);
        var stream = await service.GetFileStreamAsync("non/existent/path");
        Assert.Null(stream);
    }
}
