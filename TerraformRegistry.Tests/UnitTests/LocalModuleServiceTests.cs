using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class LocalModuleServiceTests
{
    private readonly IConfiguration _configuration;
    private readonly Mock<IDatabaseService> _mockDbService;
    private readonly Mock<ILogger<LocalModuleService>> _mockLogger;
    private readonly string _testModulePath;

    public LocalModuleServiceTests()
    {
        _mockDbService = new Mock<IDatabaseService>();
        _mockLogger = new Mock<ILogger<LocalModuleService>>();
        var inMemorySettings = new Dictionary<string, string>
        {
            { "ModuleStoragePath", Path.Combine(Path.GetTempPath(), "modules_test") }
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _testModulePath = _configuration["ModuleStoragePath"];
        if (Directory.Exists(_testModulePath)) Directory.Delete(_testModulePath, true);
    }

    // Verifies that the constructor creates the module storage directory if it does not exist
    [Fact]
    public void Constructor_CreatesModuleStorageDirectory()
    {
        // Arrange/Act
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        // Assert
        Assert.True(Directory.Exists(_testModulePath));
    }

    // Verifies that the constructor logs the storage path being used
    [Fact]
    public void Constructor_LogsStoragePath()
    {
        // Arrange/Act
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(_testModulePath)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    // Verifies that ListModulesAsync delegates to the database service and returns the expected result
    [Fact]
    public async Task ListModulesAsync_DelegatesToDatabaseService()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var request = new ModuleSearchRequest();
        var expected = new ModuleList
        {
            Modules = new List<ModuleListItem>(),
            Meta = new Dictionary<string, string>()
        };
        _mockDbService.Setup(x => x.ListModulesAsync(request)).ReturnsAsync(expected);
        // Act
        var result = await service.ListModulesAsync(request);
        // Assert
        Assert.Equal(expected, result);
    }

    // Verifies that GetModuleAsync delegates to the database service and returns the expected module
    [Fact]
    public async Task GetModuleAsync_DelegatesToDatabaseService()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var expected = new Module
        {
            Id = "id",
            Owner = "owner",
            Namespace = "namespace",
            Name = "name",
            Version = "1.0.0",
            Provider = "provider",
            Description = "desc",
            Source = null,
            PublishedAt = DateTime.UtcNow.ToString("o"),
            Versions = new List<string>(),
            Root = "root",
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>(),
            DownloadUrl = null
        };
        _mockDbService.Setup(x => x.GetModuleAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(expected);
        // Act
        var result = await service.GetModuleAsync("ns", "name", "provider", "1.0.0");
        // Assert
        Assert.Equal(expected, result);
    }

    // Verifies that GetModuleVersionsAsync delegates to the database service and returns the expected versions
    [Fact]
    public async Task GetModuleVersionsAsync_DelegatesToDatabaseService()
    {        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var expected = new ModuleVersions
        {
            Modules = new List<ModuleVersionInfo>
            {
                new ModuleVersionInfo
                {
                    Versions = new List<VersionInfo>()
                }
            }
        };
        _mockDbService.Setup(x => x.GetModuleVersionsAsync("ns", "name", "provider")).ReturnsAsync(expected);
        // Act
        var result = await service.GetModuleVersionsAsync("ns", "name", "provider");
        // Assert
        Assert.Equal(expected, result);
    }

    // Verifies that GetModuleDownloadPathAsync returns null if the module is not found in the database
    [Fact]
    public async Task GetModuleDownloadPathAsync_ReturnsNullIfModuleNotFound()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);
        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");
        // Assert
        Assert.Null(result);
    }

    // Verifies that GetModuleDownloadPathAsync returns a download link and stores a token if the module exists
    [Fact]
    public async Task GetModuleDownloadPathAsync_ReturnsDownloadLinkAndStoresToken()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = "fakepath",
            PublishedAt = DateTime.UtcNow,
            Dependencies = new List<string>()
        };
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");
        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("/module/download?token=", result);
    }

    // Verifies that TryGetFilePathFromToken returns false and an empty file path for an invalid token
    [Fact]
    public void TryGetFilePathFromToken_ReturnsFalseForInvalidToken()
    {
        // Act
        var found = LocalModuleService.TryGetFilePathFromToken("notatoken", out var filePath);
        // Assert
        Assert.False(found);
        Assert.Equal(string.Empty, filePath);
    }

    // Verifies that UploadModuleAsyncImpl saves the file and adds the module to the database
    [Fact]
    public async Task UploadModuleAsyncImpl_SavesFileAndAddsToDatabase()
    {
        // Arrange
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var ns = "ns";
        var name = "name";
        var provider = "provider";
        var version = "1.0.0";
        var desc = "desc";
        var content = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));
        _mockDbService.Setup(x => x.AddModuleAsync(It.IsAny<ModuleStorage>())).ReturnsAsync(true);
        // Act
        var result = await service.CallUploadModuleAsyncImpl(ns, name, provider, version, content, desc);
        // Assert
        Assert.True(result);
        var filePath = Path.Combine(_testModulePath, ns, $"{name}-{provider}-{version}.zip");
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteModuleAsyncImpl_Removes_Db_Entry_And_File()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var filePath = Path.Combine(_testModulePath, "ns", "name-provider-1.0.0.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "dummy");

        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = filePath,
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };

        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        _mockDbService.Setup(x => x.RemoveModuleAsync(storage)).ReturnsAsync(true);

        var result = await service.CallDeleteModuleAsyncImpl("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        Assert.False(File.Exists(filePath));
        _mockDbService.Verify(x => x.RemoveModuleAsync(storage), Times.Once);
    }

    [Fact]
    public async Task DeleteModuleAsyncImpl_Cleans_Orphaned_Db_Entry_When_File_Missing()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var filePath = Path.Combine(_testModulePath, "ns", "name-provider-1.0.0.zip");

        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = filePath,
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };

        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        _mockDbService.Setup(x => x.RemoveModuleAsync(storage)).ReturnsAsync(true);

        var result = await service.CallDeleteModuleAsyncImpl("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        _mockDbService.Verify(x => x.RemoveModuleAsync(storage), Times.Once);
    }

    [Fact]
    public async Task DeleteModuleAsyncImpl_Cleans_Orphaned_File_When_Db_Entry_Missing()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var filePath = Path.Combine(_testModulePath, "ns", "name-provider-1.0.0.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "dummy");

        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync((ModuleStorage?)null);

        var result = await service.CallDeleteModuleAsyncImpl("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        Assert.False(File.Exists(filePath));
        _mockDbService.Verify(x => x.RemoveModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    // Helper to expose protected method for testing
    private class TestableLocalModuleService : LocalModuleService
    {
        public TestableLocalModuleService(IConfiguration c, IDatabaseService d, ILogger<LocalModuleService> l) : base(c,
            d, l)
        {
        }

        public Task<bool> CallUploadModuleAsyncImpl(string ns, string name, string provider, string version,
            Stream content, string desc, bool replace = false)
        {
            return base.UploadModuleAsyncImpl(ns, name, provider, version, content, desc, replace);
        }

        public Task<bool> CallDeleteModuleAsyncImpl(string ns, string name, string provider, string version)
        {
            return base.DeleteModuleAsyncImpl(ns, name, provider, version);
        }
    }
}
