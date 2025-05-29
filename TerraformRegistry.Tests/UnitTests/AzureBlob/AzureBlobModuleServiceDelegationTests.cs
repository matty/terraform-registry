using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobModuleServiceDelegationTests
{
    private const string ContainerName = "test-container";
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<BlobContainerClient> _mockContainerClient = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService = new();
    private readonly Mock<ILogger<AzureBlobModuleService>> _mockLogger = new();

    public AzureBlobModuleServiceDelegationTests()
    {
        _mockConfiguration.Setup(c => c["AzureStorage:ContainerName"]).Returns(ContainerName);
        _mockConfiguration.Setup(c => c["AzureStorage:SasTokenExpiryMinutes"]).Returns("5");
        _mockBlobServiceClient.Setup(s => s.GetBlobContainerClient(ContainerName)).Returns(_mockContainerClient.Object);
        _mockContainerClient
            .Setup(c => c.CreateIfNotExists(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(),
                default)).Returns(Mock.Of<Response<BlobContainerInfo>>());
    }

    private AzureBlobModuleService CreateService()
    {
        return new AzureBlobModuleService(
            _mockConfiguration.Object,
            _mockDatabaseService.Object,
            _mockLogger.Object,
            _mockBlobServiceClient.Object);
    }

    [Fact]
    public async Task ListModulesAsync_Delegates_To_DatabaseService()
    {
        var request = new ModuleSearchRequest();
        var expected = new ModuleList
        {
            Modules = new List<ModuleListItem>(),
            Meta = new Dictionary<string, string>()
        };
        _mockDatabaseService.Setup(x => x.ListModulesAsync(request)).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.ListModulesAsync(request);
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.ListModulesAsync(request), Times.Once);
    }

    [Fact]
    public async Task GetModuleAsync_Delegates_To_DatabaseService()
    {
        var expected = new Module
        {
            Id = string.Empty,
            Owner = string.Empty,
            Namespace = string.Empty,
            Name = string.Empty,
            Version = string.Empty,
            Provider = string.Empty,
            PublishedAt = string.Empty,
            Versions = new List<string>(),
            Root = string.Empty,
            Submodules = new List<ModuleSubmodule>(),
            Providers = new Dictionary<string, string>()
        };
        _mockDatabaseService.Setup(x => x.GetModuleAsync("ns", "name", "provider", "1.0.0")).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.GetModuleAsync("ns", "name", "provider", "1.0.0");
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.GetModuleAsync("ns", "name", "provider", "1.0.0"), Times.Once);
    }

    [Fact]
    public async Task GetModuleVersionsAsync_Delegates_To_DatabaseService()
    {
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
        _mockDatabaseService.Setup(x => x.GetModuleVersionsAsync("ns", "name", "provider")).ReturnsAsync(expected);
        var service = CreateService();
        var result = await service.GetModuleVersionsAsync("ns", "name", "provider");
        Assert.Equal(expected, result);
        _mockDatabaseService.Verify(x => x.GetModuleVersionsAsync("ns", "name", "provider"), Times.Once);
    }
}