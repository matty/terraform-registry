using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Publishing;
using Xunit;

namespace TerraformRegistry.Tests.UnitTests;

public class GitHubVcsServiceSyncTests
{
    [Fact]
    public async Task SyncSourceAsync_PublishesMissingTagsAndUpdatesSyncState()
    {
        var source = new VcsSource
        {
            Id = Guid.NewGuid(),
            UserId = "creator-1",
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            RepoOwner = "acme",
            RepoName = "terraform-network",
            ConnectionId = Guid.NewGuid(),
            IsActive = true,
            TagPattern = "v*"
        };

        var sourceService = new Mock<IVcsSourceService>();
        sourceService.Setup(x => x.GetAsync(source.Id)).ReturnsAsync(source);
        sourceService
            .Setup(x => x.UpdateSyncStateAsync(source.Id, "succeeded", "1.1.0", null))
            .ReturnsAsync(new VcsSource
            {
                Id = source.Id,
                UserId = source.UserId,
                Namespace = source.Namespace,
                Name = source.Name,
                Provider = source.Provider,
                RepoOwner = source.RepoOwner,
                RepoName = source.RepoName,
                ConnectionId = source.ConnectionId,
                IsActive = source.IsActive,
                TagPattern = source.TagPattern,
                LastSyncStatus = "succeeded",
                LastPublishedVersion = "1.1.0",
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            });

        var connectionService = new Mock<IVcsConnectionService>();
        connectionService.Setup(x => x.GetConnectionAsync(source.ConnectionId)).ReturnsAsync(new VcsConnection
        {
            Id = source.ConnectionId,
            Provider = "github",
            Label = "GitHub",
            IsActive = true,
            WebhookSecret = "secret",
            PatEncrypted = null
        });

        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleVersionsAsync("acme", "network", "aws")).ReturnsAsync(new ModuleVersions
        {
            Modules =
            [
                new ModuleVersionInfo
                {
                    Versions = [new VersionInfo { Version = "1.0.0" }]
                }
            ]
        });

        var publishCoordinator = new Mock<IModulePublishCoordinator>();
        publishCoordinator
            .Setup(x => x.PublishAsync(It.Is<ModulePublishRequest>(request =>
                    request.Namespace == "acme"
                    && request.Name == "network"
                    && request.Provider == "aws"
                    && request.Version == "1.1.0"
                    && request.Metadata.Source!.Kind == "vcs-tag"),
                CancellationToken.None))
            .ReturnsAsync(true);

        var httpFactory = new TestHttpClientFactory(new HttpClient(new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/tags", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"name\":\"v1.1.0\"},{\"name\":\"not-a-version\"}]", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
        })));

        var service = new GitHubVcsService(
            sourceService.Object,
            connectionService.Object,
            publishCoordinator.Object,
            httpFactory,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<GitHubVcsService>.Instance);

        var result = await service.SyncSourceAsync(source.Id, null, false, "user-123", CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.PublishedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal("1.1.0", result.LatestVersion);
        sourceService.Verify(x => x.UpdateSyncStateAsync(source.Id, "succeeded", "1.1.0", null), Times.Once);
    }

    [Fact]
    public async Task SyncSourceAsync_FollowsPaginatedGitHubTagResults()
    {
        var source = new VcsSource
        {
            Id = Guid.NewGuid(),
            UserId = "creator-1",
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            RepoOwner = "acme",
            RepoName = "terraform-network",
            ConnectionId = Guid.NewGuid(),
            IsActive = true,
            TagPattern = "v*"
        };

        var pageOneTags = Enumerable.Range(0, 100)
            .Select(index => $"{{\"name\":\"draft-{index}\"}}");

        var sourceService = new Mock<IVcsSourceService>();
        sourceService.Setup(x => x.GetAsync(source.Id)).ReturnsAsync(source);
        sourceService
            .Setup(x => x.UpdateSyncStateAsync(source.Id, "succeeded", "1.1.0", null))
            .ReturnsAsync(source);

        var connectionService = new Mock<IVcsConnectionService>();
        connectionService.Setup(x => x.GetConnectionAsync(source.ConnectionId)).ReturnsAsync(new VcsConnection
        {
            Id = source.ConnectionId,
            Provider = "github",
            Label = "GitHub",
            IsActive = true,
            WebhookSecret = "secret",
            PatEncrypted = null
        });

        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleVersionsAsync("acme", "network", "aws")).ReturnsAsync(new ModuleVersions
        {
            Modules =
            [
                new ModuleVersionInfo
                {
                    Versions = [new VersionInfo { Version = "1.0.0" }]
                }
            ]
        });

        var publishCoordinator = new Mock<IModulePublishCoordinator>();
        publishCoordinator.Setup(x => x.PublishAsync(
                It.Is<ModulePublishRequest>(request => request.Version == "1.1.0"),
                CancellationToken.None))
            .ReturnsAsync(true);

        var requestedPages = new List<string>();
        var httpFactory = new TestHttpClientFactory(new HttpClient(new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/tags", StringComparison.Ordinal))
            {
                requestedPages.Add(request.RequestUri.Query);
                var page = request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal)
                    ? "[{\"name\":\"v1.1.0\"}]"
                    : $"[{string.Join(",", pageOneTags)}]";

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(page, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
        })));

        var service = new GitHubVcsService(
            sourceService.Object,
            connectionService.Object,
            moduleService.Object,
            publishCoordinator.Object,
            httpFactory,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            NullLogger<GitHubVcsService>.Instance);

        var result = await service.SyncSourceAsync(source.Id, null, false, "user-123", CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.PublishedCount);
        Assert.Equal("1.1.0", result.LatestVersion);
        Assert.Contains("?per_page=100&page=1", requestedPages);
        Assert.Contains("?per_page=100&page=2", requestedPages);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
