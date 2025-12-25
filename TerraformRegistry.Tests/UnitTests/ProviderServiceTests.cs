using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class ProviderServiceTests
{
    private readonly Mock<IDatabaseService> _mockDb;
    private readonly Mock<IProviderStorageService> _mockStorage;
    private readonly Mock<ILogger<ProviderService>> _mockLogger;
    private readonly IConfiguration _config;
    private readonly ProviderService _service;

    public ProviderServiceTests()
    {
        _mockDb = new Mock<IDatabaseService>();
        _mockStorage = new Mock<IProviderStorageService>();
        _mockLogger = new Mock<ILogger<ProviderService>>();
        _config = new ConfigurationBuilder().Build();

        _service = new ProviderService(_mockDb.Object, _mockLogger.Object, _config, _mockStorage.Object);
    }

    [Fact]
    public async Task GetProviderVersionsAsync_DelegatesToDb()
    {
        var expected = new ProviderVersions();
        _mockDb.Setup(x => x.GetProviderVersionsAsync("ns", "type")).ReturnsAsync(expected);

        var result = await _service.GetProviderVersionsAsync("ns", "type");
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task GetProviderPackageAsync_UpdatesDownloadUrl_IfStorageReturnsDynamicUrl()
    {
        var package = new ProviderPackage { DownloadUrl = "original" };
        _mockDb.Setup(x => x.GetProviderPackageAsync("ns", "type", "1.0.0", "linux", "amd64"))
            .ReturnsAsync(package);

        _mockStorage.Setup(x => x.GetProviderDownloadUrlAsync("ns", "type", "1.0.0", "linux", "amd64"))
            .ReturnsAsync("http://dynamic.url");

        var result = await _service.GetProviderPackageAsync("ns", "type", "1.0.0", "linux", "amd64");

        Assert.Equal("http://dynamic.url", result.DownloadUrl);
    }

    [Fact]
    public async Task UploadProviderAsync_ThrowsArgumentException_IfKeyNotFound()
    {
        _mockDb.Setup(x => x.GetGpgKeyAsync("ns", "keyid")).ReturnsAsync((GpgKey?)null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UploadProviderAsync("ns", "type", "1.0.0", "linux", "amd64", "file", Stream.Null, "sha", "keyid"));
    }

    [Fact]
    public async Task UploadProviderAsync_UploadsAndSavesToDb_IfSuccess()
    {
        _mockDb.Setup(x => x.GetGpgKeyAsync("ns", "keyid")).ReturnsAsync(new GpgKey());
        _mockStorage.Setup(x => x.UploadProviderAsync("ns", "type", "1.0.0", "linux", "amd64", It.IsAny<Stream>()))
            .ReturnsAsync("storage/path");

        var result = await _service.UploadProviderAsync("ns", "type", "1.0.0", "linux", "amd64", "file.zip", Stream.Null, "sha", "keyid");

        Assert.Equal("storage/path", result.DownloadUrl);
        Assert.Equal("5.0", result.Protocols[0]); // Default protocol check

        _mockDb.Verify(x => x.AddProviderPackageAsync(
            "ns", "type", "1.0.0", "linux", "amd64", "file.zip", "storage/path", "sha", It.IsAny<string>(), "keyid"),
            Times.Once);
    }
}
