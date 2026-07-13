using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Services.Publishing;
using Xunit;

namespace TerraformRegistry.Tests.UnitTests;

public class ModulePublishCoordinatorTests
{
    private static readonly HttpClient StaticOkClient = new(new StaticOkHandler());

    [Fact]
    public async Task PublishAsyncDoesNotCreateSideEffectsWhenArchiveValidationFails()
    {
        var validator = new Mock<IArchiveIngestionValidator>();
        validator.Setup(x => x.PrepareAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Archive expanded content exceeds the configured limit."));
        var moduleService = new Mock<IModuleService>();

        var coordinator = new ModulePublishCoordinator(
            moduleService.Object,
            Mock.Of<IModuleExtractionService>(),
            CreateWebhookDispatcher(),
            Mock.Of<IAuditService>(),
            NullLogger<ModulePublishCoordinator>.Instance,
            validator.Object);
        await using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PublishAsync(new ModulePublishRequest
        {
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.2.3",
            ModuleContent = content,
            Metadata = new ModuleArtifactMetadata { Source = new ModuleSourceInfo { Kind = "api-upload" } }
        }, CancellationToken.None));

        moduleService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsyncUploadsModuleAndQueuesExtraction()
    {
        var moduleService = new Mock<IModuleService>();
        moduleService
            .Setup(x => x.UploadModuleAsync(
                "acme",
                "network",
                "aws",
                "1.2.3",
                It.IsAny<Stream>(),
                "VPC module",
                false,
                It.Is<ModuleArtifactMetadata>(m => m.Source!.Kind == "api-upload")))
            .ReturnsAsync(true);

        var extraction = new Mock<IModuleExtractionService>();
        extraction
            .Setup(x => x.QueueAsync(
                new ModuleExtractionRequest("acme", "network", "aws", "1.2.3"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var audit = new Mock<IAuditService>();
        var webhookService = new Mock<IWebhookService>();
        webhookService
            .Setup(x => x.GetActiveWebhooksForEventAsync("module.published"))
            .ReturnsAsync(Array.Empty<Webhook>());

        var webhookDispatcher = new WebhookDispatcher(
            webhookService.Object,
            new TestHttpClientFactory(StaticOkClient),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["BaseUrl"] = "https://registry.example.com" })
                .Build(),
            new WebhookUrlValidator(
                Options.Create(new WebhookSecurityOptions { AllowPrivateNetworks = true }),
                new StaticWebhookHostResolver(IPAddress.Loopback),
                NullLogger<WebhookUrlValidator>.Instance),
            NullLogger<WebhookDispatcher>.Instance);

        var coordinator = new ModulePublishCoordinator(
            moduleService.Object,
            extraction.Object,
            webhookDispatcher,
            audit.Object,
            NullLogger<ModulePublishCoordinator>.Instance);

        await using var content = new MemoryStream([1, 2, 3]);

        var published = await coordinator.PublishAsync(new ModulePublishRequest
        {
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.2.3",
            Description = "VPC module",
            ModuleContent = content,
            Replace = false,
            ActorUserId = "user-123",
            Metadata = new ModuleArtifactMetadata
            {
                Source = new ModuleSourceInfo { Kind = "api-upload" }
            }
        }, CancellationToken.None);

        Assert.True(published);
        extraction.Verify(x => x.QueueAsync(
            new ModuleExtractionRequest("acme", "network", "aws", "1.2.3"),
            It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.LogAsync(
            "user-123",
            "module.published",
            "module",
            "acme/network/aws/1.2.3",
            It.IsAny<object>(),
            null), Times.Once);
    }

    [Fact]
    public async Task PublishAsyncReturnsFalseWhenStorageRejectsDuplicate()
    {
        var moduleService = new Mock<IModuleService>();
        moduleService
            .Setup(x => x.UploadModuleAsync(
                "acme",
                "network",
                "aws",
                "1.2.3",
                It.IsAny<Stream>(),
                "VPC module",
                false,
                It.IsAny<ModuleArtifactMetadata>()))
            .ReturnsAsync(false);

        var webhookService = new Mock<IWebhookService>();
        webhookService
            .Setup(x => x.GetActiveWebhooksForEventAsync("module.published"))
            .ReturnsAsync(Array.Empty<Webhook>());

        var coordinator = new ModulePublishCoordinator(
            moduleService.Object,
            Mock.Of<IModuleExtractionService>(),
            new WebhookDispatcher(
                webhookService.Object,
                new TestHttpClientFactory(new HttpClient(new StaticOkHandler())),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal))
                    .Build(),
                new WebhookUrlValidator(
                    Options.Create(new WebhookSecurityOptions { AllowPrivateNetworks = true }),
                    new StaticWebhookHostResolver(IPAddress.Loopback),
                    NullLogger<WebhookUrlValidator>.Instance),
                NullLogger<WebhookDispatcher>.Instance),
            Mock.Of<IAuditService>(),
            NullLogger<ModulePublishCoordinator>.Instance);

        await using var content = new MemoryStream([4, 5, 6]);

        var published = await coordinator.PublishAsync(new ModulePublishRequest
        {
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.2.3",
            Description = "VPC module",
            ModuleContent = content,
            Metadata = new ModuleArtifactMetadata
            {
                Source = new ModuleSourceInfo { Kind = "api-upload" }
            }
        }, CancellationToken.None);

        Assert.False(published);
    }

    private sealed class StaticOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static WebhookDispatcher CreateWebhookDispatcher()
    {
        var webhookService = new Mock<IWebhookService>();
        webhookService.Setup(x => x.GetActiveWebhooksForEventAsync("module.published"))
            .ReturnsAsync(Array.Empty<Webhook>());
        return new WebhookDispatcher(
            webhookService.Object,
            new TestHttpClientFactory(new HttpClient(new StaticOkHandler())),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            new WebhookUrlValidator(
                Options.Create(new WebhookSecurityOptions { AllowPrivateNetworks = true }),
                new StaticWebhookHostResolver(IPAddress.Loopback),
                NullLogger<WebhookUrlValidator>.Instance),
            NullLogger<WebhookDispatcher>.Instance);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticWebhookHostResolver(IPAddress address) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { address });
    }
}
