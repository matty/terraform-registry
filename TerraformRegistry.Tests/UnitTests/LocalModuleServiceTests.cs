using System.IO.Compression;
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
        var testModulePath = Path.Combine(Path.GetTempPath(), "modules_test");
        var inMemorySettings = new Dictionary<string, string?>
(StringComparer.Ordinal)
        {
            { "ModuleStoragePath", testModulePath },
            { "ArtifactDownloadTokens:SigningKey", "test-signing-key-that-is-long-enough-to-be-safe-0123456789" }
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _testModulePath = testModulePath;
        if (Directory.Exists(_testModulePath)) Directory.Delete(_testModulePath, true);
    }

    // Storage work is deferred until hosted startup completes database migration.
    [Fact]
    public async Task InitializeStorageCreatesModuleStorageDirectory()
    {
        // Arrange/Act
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        Assert.False(Directory.Exists(_testModulePath));

        await service.InitializeStorageAsync(CancellationToken.None);

        // Assert
        Assert.True(Directory.Exists(_testModulePath));
    }

    [Fact]
    public async Task InitializationDefersLocalArtifactRecoveryUntilReconciliation()
    {
        var namespaceDirectory = Path.Join(_testModulePath, "acme");
        Directory.CreateDirectory(namespaceDirectory);
        var archivePath = Path.Join(namespaceDirectory, "network-aws-1.0.0.zip");
        using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
        }
        _mockDbService.Setup(service => service.AddModuleAsync(It.IsAny<ModuleStorage>())).ReturnsAsync(true);
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);

        await service.InitializeStorageAsync(CancellationToken.None);

        _mockDbService.Verify(database => database.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
        await service.ReconcileStorageAsync(CancellationToken.None);
        _mockDbService.Verify(database => database.AddModuleAsync(It.Is<ModuleStorage>(module =>
            module.Namespace == "acme" && module.Name == "network" && module.Provider == "aws" &&
            module.Version == "1.0.0")), Times.Once);
    }

    // Verifies that the constructor logs the storage path being used
    [Fact]
    public void ConstructorLogsStoragePath()
    {
        // Arrange/Act
        var logger = new CapturingLogger<LocalModuleService>();
        var service = new LocalModuleService(_configuration, _mockDbService.Object, logger);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains(_testModulePath, StringComparison.Ordinal));
    }

    // Verifies that ListModulesAsync delegates to the database service and returns the expected result
    [Fact]
    public async Task ListModulesAsyncDelegatesToDatabaseService()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var request = new ModuleSearchRequest();
        var expected = new ModuleList
        {
            Modules = new List<ModuleListItem>(),
            Meta = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        _mockDbService.Setup(x => x.ListModulesAsync(request)).ReturnsAsync(expected);
        // Act
        var result = await service.ListModulesAsync(request);
        // Assert
        Assert.Equal(expected, result);
    }

    // Verifies that GetModuleAsync delegates to the database service and returns the expected module
    [Fact]
    public async Task GetModuleAsyncDelegatesToDatabaseService()
    {
        // Arrange
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var expected = new TerraformModule
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
            Providers = new Dictionary<string, string>(StringComparer.Ordinal),
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
    public async Task GetModuleVersionsAsyncDelegatesToDatabaseService()
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
    public async Task GetModuleDownloadPathAsyncReturnsNullIfModuleNotFound()
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
    public async Task GetModuleDownloadPathAsyncReturnsDownloadLinkAndStoresToken()
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
            FilePath = Path.Combine(_testModulePath, "ns", "name-provider-1.0.0.zip"),
            PublishedAt = DateTime.UtcNow,
            Dependencies = new List<string>()
        };
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.FilePath)!);
        await File.WriteAllBytesAsync(storage.FilePath, [0x50, 0x4B, 0x03, 0x04]);

        // Act
        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");
        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("/module/download?token=", result);
        Assert.Contains("archive=zip", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullWhenArtifactIsMissing()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = Path.Combine(_testModulePath, "ns", "missing.zip"),
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncAddsTarGzHintForTarArtifact()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = Path.Combine(_testModulePath, "ns", "name-provider-1.0.0.tar.gz"),
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        Directory.CreateDirectory(Path.GetDirectoryName(storage.FilePath)!);
        await File.WriteAllBytesAsync(storage.FilePath, [0x1F, 0x8B]);
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");

        Assert.Contains("archive=tar.gz", result, StringComparison.Ordinal);
    }

    // Verifies that TryGetFilePathFromToken returns false and an empty file path for an invalid token
    [Fact]
    public void TryGetFilePathFromTokenReturnsFalseForInvalidToken()
    {
        // Act
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var found = service.TryGetFilePathFromToken("notatoken", out var filePath);
        // Assert
        Assert.False(found);
        Assert.Equal(string.Empty, filePath);
    }

    // Verifies that UploadModuleAsyncCore promotes a unique artifact only after the catalog commit succeeds.
    [Fact]
    public async Task UploadModuleAsyncCorePromotesFileAfterCatalogCommit()
    {
        // Arrange
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var ns = "ns";
        var name = "name";
        var provider = "provider";
        var version = "1.0.0";
        var desc = "desc";
        var content = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));
        ModuleStorage? committed = null;
        _mockDbService.Setup(x => x.GetModuleStorageAsync(ns, name, provider, version))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDbService.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .Callback<ModulePublicationAttempt, ModuleStorage, ModuleStorage?, CancellationToken>((_, module, _, _) => committed = module)
            .ReturnsAsync(true);
        // Act
        var result = await service.CallUploadModuleAsyncCore(ns, name, provider, version, content, desc);
        // Assert
        Assert.True(result);
        Assert.NotNull(committed);
        Assert.True(File.Exists(committed!.FilePath));
    }

    [Fact]
    public async Task UploadModuleAsyncCoreStagesAndCommitsAUniquePublicationAttempt()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));
        ModulePublicationAttempt? capturedAttempt = null;
        ModuleExtractionJob? capturedJob = null;
        ModuleStorage? capturedModule = null;

        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .Returns(Task.FromResult<ModuleStorage?>(null));
        _mockDbService.Setup(x => x.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Callback<ModulePublicationAttempt, ModuleExtractionJob, CancellationToken>((attempt, job, _) =>
            {
                capturedAttempt = attempt;
                capturedJob = job;
            })
            .Returns(Task.CompletedTask);
        _mockDbService.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null))
            .Callback<ModulePublicationAttempt, ModuleStorage, ModuleStorage?, CancellationToken>((_, module, _, _) => capturedModule = module)
            .ReturnsAsync(true);

        var result = await service.CallUploadModuleAsyncCore("ns", "name", "provider", "1.0.0", content, "desc");

        Assert.True(result);
        Assert.NotNull(capturedAttempt);
        Assert.NotNull(capturedJob);
        Assert.NotNull(capturedModule);
        Assert.Equal(ModulePublicationAttemptState.Staged, capturedAttempt!.State);
        Assert.Equal(capturedAttempt.Id, capturedJob!.PublicationAttemptId);
        Assert.Contains(capturedAttempt.Id.ToString("N"), capturedAttempt.StagingKey, StringComparison.Ordinal);
        Assert.Contains(capturedAttempt.Id.ToString("N"), capturedModule!.FilePath, StringComparison.Ordinal);
        Assert.True(File.Exists(capturedModule.FilePath));
    }

    [Fact]
    public async Task UploadModuleAsyncCoreKeepsCommittedArtifactWhenRequestIsCanceledAfterCommit()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("committed"));
        using var cancellation = new CancellationTokenSource();
        ModuleStorage? committedModule = null;

        _mockDbService.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), null, cancellation.Token))
            .Callback<ModulePublicationAttempt, ModuleStorage, ModuleStorage?, CancellationToken>((_, module, _, _) =>
            {
                committedModule = module;
                cancellation.Cancel();
            })
            .ReturnsAsync(true);

        var result = await service.CallUploadModuleAsyncCore("ns", "name", "provider", "1.0.0", content, "desc",
            cancellationToken: cancellation.Token);

        Assert.True(result);
        Assert.NotNull(committedModule);
        Assert.True(File.Exists(committedModule!.FilePath));
        _mockDbService.Verify(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task UploadModuleAsyncCoreRemovesOnlyItsArtifactWhenCatalogCommitLoses()
    {
        var service = new TestableLocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("loser"));
        ModuleStorage? attemptedModule = null;
        ModulePublicationAttempt? attemptedPublication = null;
        var existing = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "winner",
            FilePath = Path.Join(_testModulePath, "ns/.published/winner/module.zip"),
            PublishedAt = DateTime.UtcNow.AddMinutes(-1),
            Dependencies = []
        };
        Directory.CreateDirectory(Path.GetDirectoryName(existing.FilePath)!);
        await File.WriteAllTextAsync(existing.FilePath, "winner");

        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync(existing);
        _mockDbService.Setup(x => x.CreatePublicationAttemptWithExtractionJobAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleExtractionJob>()))
            .Callback<ModulePublicationAttempt, ModuleExtractionJob, CancellationToken>((attempt, _, _) => attemptedPublication = attempt)
            .Returns(Task.CompletedTask);
        _mockDbService.Setup(x => x.TryCommitStagedPublicationAsync(
                It.IsAny<ModulePublicationAttempt>(), It.IsAny<ModuleStorage>(), existing))
            .Callback<ModulePublicationAttempt, ModuleStorage, ModuleStorage?, CancellationToken>((_, module, _, _) => attemptedModule = module)
            .ReturnsAsync(false);
        _mockDbService.Setup(x => x.TryFailStagedPublicationAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await service.CallUploadModuleAsyncCore("ns", "name", "provider", "1.0.0", content, "desc",
            replace: true);

        Assert.False(result);
        Assert.NotNull(attemptedModule);
        Assert.False(File.Exists(attemptedModule!.FilePath));
        Assert.True(File.Exists(existing.FilePath));
        _mockDbService.Verify(x => x.TryFailStagedPublicationAsync(attemptedPublication!.Id,
            It.Is<string>(reason => reason.Contains("Catalog changed", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task PurgeModuleVersionAsyncDeletesOwnedArtifactBeforeRemovingCatalogRow()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var filePath = Path.Join(_testModulePath, "ns/.published/attempt/module.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "artifact");
        var module = new ModuleStorage
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
        _mockDbService.Setup(x => x.GetModuleStorageIncludingDeletedAsync("ns", "name", "provider", "1.0.0"))
            .ReturnsAsync(module);
        _mockDbService.Setup(x => x.RemoveModuleAsync(module))
            .Callback(() => Assert.False(File.Exists(filePath)))
            .ReturnsAsync(true);

        var result = await service.PurgeModuleVersionAsync("ns", "name", "provider", "1.0.0");

        Assert.True(result);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task UploadModuleAsyncRejectsTraversalNamespace()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadModuleAsync("../outside", "name", "provider", "1.0.0", content, "desc"));

        Assert.Contains("Invalid namespace", ex.Message, StringComparison.Ordinal);
        _mockDbService.Verify(x => x.AddModuleAsync(It.IsAny<ModuleStorage>()), Times.Never);
    }

    [Fact]
    public async Task GetModuleDownloadPathAsyncReturnsNullForPathOutsideStorageRoot()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.zip");
        var storage = new ModuleStorage
        {
            Namespace = "ns",
            Name = "name",
            Provider = "provider",
            Version = "1.0.0",
            Description = "desc",
            FilePath = outsidePath,
            PublishedAt = DateTime.UtcNow,
            Dependencies = []
        };
        _mockDbService.Setup(x => x.GetModuleStorageAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(storage);

        var result = await service.GetModuleDownloadPathAsync("ns", "name", "provider", "1.0.0");

        Assert.Null(result);
    }

    [Fact]
    public async Task OpenModulePackageStreamAsyncReturnsStoredZipContent()
    {
        var service = new LocalModuleService(_configuration, _mockDbService.Object, _mockLogger.Object);
        var namespacePath = Path.Combine(_testModulePath, "ns");
        Directory.CreateDirectory(namespacePath);
        var filePath = Path.Combine(namespacePath, "name-provider-1.0.0.zip");
        await File.WriteAllBytesAsync(filePath, [0x50, 0x4B, 0x03, 0x04]);
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

        await using var stream = await service.OpenModulePackageStreamAsync("ns", "name", "provider", "1.0.0");

        Assert.NotNull(stream);
        Assert.Equal(4, stream!.Length);
    }

    // Helper to expose protected method for testing
    private sealed class TestableLocalModuleService : LocalModuleService
    {
        public TestableLocalModuleService(IConfiguration c, IDatabaseService d, ILogger<LocalModuleService> l) : base(c,
            d, l)
        {
        }

        public Task<bool> CallUploadModuleAsyncCore(string ns, string name, string provider, string version,
            Stream content, string desc, bool replace = false, CancellationToken cancellationToken = default)
        {
            return base.UploadModuleAsyncCore(ns, name, provider, version, content, desc, replace, null,
                cancellationToken);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
