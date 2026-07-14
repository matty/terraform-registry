using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.UnitTests.S3;

public class S3ModuleServiceDelegationTests
{
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<S3ModuleService>> _mockLogger = new();
    private readonly Mock<IAmazonS3> _mockS3Client = new();

    public S3ModuleServiceDelegationTests()
    {
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
            .ReturnsAsync(new ListObjectsV2Response());
    }

    private S3ModuleService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["S3:BucketName"] = "modules",
                ["S3:Region"] = "eu-west-2"
            })
            .Build();

        return new S3ModuleService(configuration, _mockDatabaseService.Object, _mockLogger.Object, _mockS3Client.Object);
    }

    [Fact]
    public async Task ListModulesAsyncDelegatesToDatabaseService()
    {
        var request = new ModuleSearchRequest();
        var expected = new ModuleList { Modules = [], Meta = new Dictionary<string, string>(StringComparer.Ordinal) };
        _mockDatabaseService.Setup(x => x.ListModulesAsync(request)).ReturnsAsync(expected);

        var service = CreateService();
        var result = await service.ListModulesAsync(request);

        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.ListModulesAsync(request), Times.Once);
    }

    [Fact]
    public async Task ListModulesAsyncPassesCancellationToDatabaseService()
    {
        var request = new ModuleSearchRequest();
        using var cancellation = new CancellationTokenSource();
        _mockDatabaseService
            .Setup(x => x.ListModulesAsync(request, cancellation.Token))
            .ReturnsAsync(new ModuleList { Modules = [], Meta = [] });

        var service = CreateService();
        await service.ListModulesAsync(request, cancellation.Token);

        _mockDatabaseService.Verify(x => x.ListModulesAsync(request, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleAsyncPassesCancellationToDatabaseService()
    {
        using var cancellation = new CancellationTokenSource();
        _mockDatabaseService
            .Setup(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", cancellation.Token))
            .Returns(Task.FromResult<TerraformModule?>(null));

        await CreateService().GetModuleAsync("ns", "name", "aws", "1.0.0", cancellation.Token);

        _mockDatabaseService.Verify(
            x => x.GetModuleAsync("ns", "name", "aws", "1.0.0", cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleVersionsAsyncPassesCancellationToDatabaseService()
    {
        using var cancellation = new CancellationTokenSource();
        _mockDatabaseService
            .Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token))
            .ReturnsAsync(new ModuleVersions { Modules = [] });

        await CreateService().GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token);

        _mockDatabaseService.Verify(
            x => x.GetModuleVersionsAsync("ns", "name", "aws", cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetModuleAsyncDelegatesToDatabaseService()
    {
        var expected = new TerraformModule
        {
            Id = "id",
            Owner = "owner",
            Namespace = "ns",
            Name = "name",
            Version = "1.0.0",
            Provider = "aws",
            PublishedAt = DateTime.UtcNow.ToString("o"),
            Versions = ["1.0.0"],
            Root = "root",
            Submodules = [],
            Providers = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        _mockDatabaseService.Setup(x => x.GetModuleAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(expected);

        var service = CreateService();
        var result = await service.GetModuleAsync("ns", "name", "aws", "1.0.0");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetModuleVersionsAsyncDelegatesToDatabaseService()
    {
        var expected = new ModuleVersions
        {
            Modules =
            [
                new ModuleVersionInfo
                {
                    Versions =
                    [
                        new VersionInfo { Version = "1.0.0" }
                    ]
                }
            ]
        };
        _mockDatabaseService.Setup(x => x.GetModuleVersionsAsync("ns", "name", "aws")).ReturnsAsync(expected);

        var service = CreateService();
        var result = await service.GetModuleVersionsAsync("ns", "name", "aws");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ListDeletedModulesAsyncDelegatesToDatabaseService()
    {
        var request = new ModuleSearchRequest();
        var expected = new ModuleList { Modules = [], Meta = new Dictionary<string, string>(StringComparer.Ordinal) };
        _mockDatabaseService.Setup(x => x.ListDeletedModulesAsync(request)).ReturnsAsync(expected);

        var service = CreateService();
        var result = await service.ListDeletedModulesAsync(request);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task DeleteAndRestoreDelegateToDatabaseService()
    {
        _mockDatabaseService.Setup(x => x.SoftDeleteModuleAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(true);
        _mockDatabaseService.Setup(x => x.RestoreModuleAsync("ns", "name", "aws", "1.0.0")).ReturnsAsync(true);

        var service = CreateService();

        Assert.True(await service.DeleteModuleVersionAsync("ns", "name", "aws", "1.0.0"));
        Assert.True(await service.RestoreModuleVersionAsync("ns", "name", "aws", "1.0.0"));
    }

    [Fact]
    public async Task UpdateModuleDescriptionAsyncDelegatesToDatabaseService()
    {
        _mockDatabaseService.Setup(x => x.UpdateModuleDescriptionAsync("ns", "name", "aws", "new-desc")).ReturnsAsync(true);

        var service = CreateService();
        var result = await service.UpdateModuleDescriptionAsync("ns", "name", "aws", "new-desc");

        Assert.True(result);
    }
}
