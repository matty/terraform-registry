using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;
using TerraformRegistry.Services.Publishing;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class ModuleMirrorServiceTests
{
    [Fact]
    public async Task ModuleVersionsMergeLocalAndUpstreamWithLocalWinningConflict()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondJson(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/versions",
            VersionsJson("2.0.0", "3.0.0"));
        var repository = new Mock<IModuleMirrorRepository>();
        var service = CreateService(http, repository: repository);
        var local = ModuleVersions("1.0.0", "2.0.0");

        var result = await service.GetModuleVersionsAsync("hashicorp", "vpc", "aws", local, CancellationToken.None);

        var versions = result.Modules.Single().Versions.Select(x => x.Version).ToArray();
        Assert.Equal(["1.0.0", "2.0.0", "3.0.0"], versions);
        Assert.Equal(1, versions.Count(x => x == "2.0.0"));
        repository.Verify(x => x.UpsertModuleVersionsAsync(It.Is<MirrorModuleVersions>(cached =>
            cached.State == "ready" &&
            cached.VersionsJson.Contains("\"3.0.0\"", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task ExactModuleFallsBackToUpstreamAndRewritesDownloadUrl()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondJson(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3",
            JsonSerializer.Serialize(new TerraformModule
            {
                Id = "hashicorp/vpc/aws/1.2.3",
                Owner = "hashicorp",
                Namespace = "hashicorp",
                Name = "vpc",
                Provider = "aws",
                Version = "1.2.3",
                Description = "Upstream VPC",
                Source = "https://github.com/hashicorp/vpc",
                PublishedAt = "2026-01-01T00:00:00Z",
                Versions = ["1.2.3"],
                Root = "",
                Submodules = [],
                Providers = [],
                DownloadUrl = "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download"
            }));
        var service = CreateService(http);

        var result = await service.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3", null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Upstream VPC", result!.Description);
        Assert.Equal("/v1/modules/hashicorp/vpc/aws/1.2.3/download", result.DownloadUrl);
    }

    [Fact]
    public async Task ExactModuleLocalWinsAndAvoidsUpstreamLookup()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondJson(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3",
            JsonSerializer.Serialize(CreateModule("Upstream VPC")));
        var service = CreateService(http);
        var local = CreateModule("Local VPC");

        var result = await service.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3", local, CancellationToken.None);

        Assert.Same(local, result);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ExactModuleHandlerPassesLocalModuleThroughMirrorService()
    {
        var local = CreateModule("Local VPC");
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(local);
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleAsync(
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3",
                local,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var result = await ModuleHandlers.GetModule(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            moduleService.Object,
            mirror.Object,
            new DefaultHttpContext());

        var ok = Assert.IsType<Ok<TerraformModule>>(result);
        Assert.Same(local, ok.Value);
        mirror.Verify(x => x.GetModuleAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            local,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExactModuleHandlerFallsBackToMirrorWhenLocalIsMissing()
    {
        var upstream = CreateModule("Upstream VPC");
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync((TerraformModule?)null);
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleAsync(
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstream);

        var result = await ModuleHandlers.GetModule(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            moduleService.Object,
            mirror.Object,
            new DefaultHttpContext());

        var ok = Assert.IsType<Ok<TerraformModule>>(result);
        Assert.Same(upstream, ok.Value);
    }

    [Fact]
    public async Task ExactDownloadResolvesRelativeXTerraformGetAndCachesPackage()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "/archives/vpc-1.2.3.zip");
        http.RespondBytes("https://registry.example.com/archives/vpc-1.2.3.zip", [1, 2, 3], "application/zip");
        var moduleService = new Mock<IModuleService>();
        moduleService.SetupSequence(x => x.GetModuleDownloadPathAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("/module/download?token=abc");
        var publish = new Mock<IModulePublishCoordinator>();
        publish.Setup(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(http, moduleService: moduleService, publish: publish);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Equal("/module/download?token=abc", result);
        Assert.Contains(http.Requests, uri => uri.ToString() == "https://registry.example.com/archives/vpc-1.2.3.zip");
        publish.Verify(x => x.PublishAsync(It.Is<ModulePublishRequest>(request =>
            request.Namespace == "hashicorp" &&
            request.Name == "vpc" &&
            request.Provider == "aws" &&
            request.Version == "1.2.3" &&
            !request.Replace &&
            request.AuditAction == "module.mirror_cached" &&
            request.Metadata.Source != null &&
            request.Metadata.Source.Kind == "mirror" &&
            request.Metadata.Source.Origin == "registry.example.com" &&
            request.Metadata.Source.SourceUrl == "/archives/vpc-1.2.3.zip" &&
            request.Metadata.Source.ResolvedPackageUrl == "https://registry.example.com/archives/vpc-1.2.3.zip"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExactDownloadDiscoveryRedirectToPrivateHostIsNotFollowed()
    {
        var redirectingDiscovery = new AutoRedirectRecordingHttpMessageHandler();
        redirectingDiscovery.RespondRedirect(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "https://private.example.com/module-discovery");
        redirectingDiscovery.RespondDownloadHeader(
            "https://private.example.com/module-discovery",
            "https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz");
        redirectingDiscovery.RespondBytes("https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz", [1, 2, 3], "application/gzip");
        var noRedirectDiscovery = new RecordingHttpMessageHandler();
        noRedirectDiscovery.RespondRedirect(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "https://private.example.com/module-discovery");
        var publish = new Mock<IModulePublishCoordinator>();
        var service = CreateService(
            redirectingDiscovery,
            publish: publish,
            httpClientFactory: new DiscoveryClientFactory(
                unnamedClient: new HttpClient(redirectingDiscovery),
                discoveryClient: new HttpClient(noRedirectDiscovery),
                mirrorClient: new HttpClient(new RecordingHttpMessageHandler())));

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(noRedirectDiscovery.Requests, uri => uri.Host == "private.example.com");
        Assert.DoesNotContain(redirectingDiscovery.Requests, uri => uri.Host == "private.example.com");
        publish.Verify(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExactDownloadPreservesArchiveQueryHint()
    {
        var service = CreateDownloadServiceWithHeader(
            "https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz?archive=tar.gz",
            out _);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Equal("/module/download?token=abc&archive=tar.gz", result);
    }

    [Fact]
    public async Task ExactDownloadPreservesGoGetterSubdirectoryAndArchiveHint()
    {
        var service = CreateDownloadServiceWithHeader(
            "https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz//*?archive=tar.gz",
            out _);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Equal("/module/download?token=abc//*?archive=tar.gz", result);
    }

    [Fact]
    public async Task CachedMirroredDownloadPreservesArchiveOnlyHintWhenHandlerProvidesLocalPath()
    {
        var repository = new Mock<IModuleMirrorRepository>();
        repository.Setup(x => x.GetModulePackageAsync(
                "registry.example.com",
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3"))
            .ReturnsAsync(ReadyModulePackage(metadataJson: MirrorMetadata(archiveFormat: "tar.gz")));
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(CreateMirrorModule());
        var service = CreateService(new RecordingHttpMessageHandler(), repository: repository, moduleService: moduleService);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            "/module/download?token=cached",
            CancellationToken.None);

        Assert.Equal("/module/download?token=cached&archive=tar.gz", result);
    }

    [Fact]
    public async Task CachedMirroredDownloadPreservesGoGetterSuffixWhenHandlerProvidesLocalPath()
    {
        var repository = new Mock<IModuleMirrorRepository>();
        repository.Setup(x => x.GetModulePackageAsync(
                "registry.example.com",
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3"))
            .ReturnsAsync(ReadyModulePackage(metadataJson: MirrorMetadata("//*?archive=tar.gz", "tar.gz")));
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(CreateMirrorModule());
        var service = CreateService(new RecordingHttpMessageHandler(), repository: repository, moduleService: moduleService);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            "/module/download?token=cached",
            CancellationToken.None);

        Assert.Equal("/module/download?token=cached//*?archive=tar.gz", result);
    }

    [Fact]
    public async Task CachedPackageHelperPreservesArchiveOnlyHintAfterLocalLookup()
    {
        var repository = new Mock<IModuleMirrorRepository>();
        repository.Setup(x => x.GetModulePackageAsync(
                "registry.example.com",
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3"))
            .ReturnsAsync(ReadyModulePackage(metadataJson: MirrorMetadata(archiveFormat: "tar.gz")));
        var moduleService = new Mock<IModuleService>();
        moduleService.SetupSequence(x => x.GetModuleDownloadPathAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("/module/download?token=cached");
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(CreateMirrorModule());
        var service = CreateService(new RecordingHttpMessageHandler(), repository: repository, moduleService: moduleService);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Equal("/module/download?token=cached&archive=tar.gz", result);
    }

    [Fact]
    public async Task ExactDownloadDoesNotPublishWhenLocalApiUploadAppearsBeforeMirrorWrite()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz");
        http.RespondBytes("https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz", [1, 2, 3], "application/gzip");
        var moduleService = new Mock<IModuleService>();
        moduleService.SetupSequence(x => x.GetModuleDownloadPathAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("/module/download?token=local");
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(CreateModule("Concurrent API upload", "api-upload"));
        var publish = new Mock<IModulePublishCoordinator>();
        var service = CreateService(http, moduleService: moduleService, publish: publish);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Equal("/module/download?token=local", result);
        publish.Verify(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LocalNonMirroredDownloadWinsUnchanged()
    {
        var repository = new Mock<IModuleMirrorRepository>();
        repository.Setup(x => x.GetModulePackageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync((MirrorModulePackage?)null);
        var service = CreateService(new RecordingHttpMessageHandler(), repository: repository);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            "/module/download?token=local",
            CancellationToken.None);

        Assert.Equal("/module/download?token=local", result);
    }

    [Fact]
    public async Task LocalApiUploadDownloadDoesNotInheritStaleMirrorHints()
    {
        var repository = new Mock<IModuleMirrorRepository>();
        repository.Setup(x => x.GetModulePackageAsync(
                "registry.example.com",
                "hashicorp",
                "vpc",
                "aws",
                "1.2.3"))
            .ReturnsAsync(ReadyModulePackage(metadataJson: MirrorMetadata(archiveFormat: "tar.gz")));
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync(CreateModule("Local API upload", "api-upload"));
        var service = CreateService(new RecordingHttpMessageHandler(), repository: repository, moduleService: moduleService);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            "/module/download?token=local",
            CancellationToken.None);

        Assert.Equal("/module/download?token=local", result);
    }

    [Fact]
    public async Task ExactDownloadRejectsRecursiveModuleRegistryAddress()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "registry.terraform.io/hashicorp/consul/aws");
        var repository = new Mock<IModuleMirrorRepository>();
        var publish = new Mock<IModulePublishCoordinator>();
        var service = CreateService(http, repository: repository, publish: publish);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Null(result);
        publish.Verify(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.MarkModulePackageFailedAsync(
            "registry.example.com",
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            It.Is<string>(message => message.Contains("recursive", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }

    [Fact]
    public async Task ExactDownloadRejectsAbsoluteRecursiveModuleRegistryAddress()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "https://registry.terraform.io/hashicorp/consul/aws");
        var repository = new Mock<IModuleMirrorRepository>();
        var publish = new Mock<IModulePublishCoordinator>();
        var service = CreateService(http, repository: repository, publish: publish);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Null(result);
        publish.Verify(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.MarkModulePackageFailedAsync(
            "registry.example.com",
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            It.Is<string>(message => message.Contains("recursive", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }

    [Fact]
    public async Task ExactDownloadRejectsPrivateArchiveUrlBeforeFetch()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            "https://private.example.com/vpc.zip");
        http.RespondBytes("https://private.example.com/vpc.zip", [1, 2, 3], "application/zip");
        var policy = CreateRealPolicy(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["private.example.com"] = [IPAddress.Loopback]
        });
        var service = CreateService(http, policyService: policy);

        var result = await service.GetModuleDownloadPathAsync(
            "hashicorp",
            "vpc",
            "aws",
            "1.2.3",
            null,
            CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(http.Requests, uri => uri.Host == "private.example.com");
    }

    [Fact]
    public async Task LatestDownloadUsesProtocolStatusWithoutTerraformUserAgent()
    {
        var moduleService = new Mock<IModuleService>();
        moduleService.Setup(x => x.GetModuleVersionsAsync("hashicorp", "vpc", "aws"))
            .ReturnsAsync(ModuleVersions());
        var mirror = new Mock<IModuleMirrorService>();
        mirror.Setup(x => x.GetModuleVersionsAsync(
                "hashicorp",
                "vpc",
                "aws",
                It.IsAny<ModuleVersions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModuleVersions("1.0.0", "2.0.0"));
        mirror.Setup(x => x.GetModuleDownloadPathAsync(
                "hashicorp",
                "vpc",
                "aws",
                "2.0.0",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("/module/download?token=latest");
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = "generic-http-client";

        var result = await ModuleHandlers.DownloadLatestModule(
            "hashicorp",
            "vpc",
            "aws",
            moduleService.Object,
            mirror.Object,
            Mock.Of<IDatabaseService>(),
            context);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NoContent>(result);
        Assert.Equal("/module/download?token=latest", context.Response.Headers["X-Terraform-Get"]);
    }

    private static ModuleMirrorService CreateDownloadServiceWithHeader(
        string xTerraformGet,
        out RecordingHttpMessageHandler http)
    {
        http = new RecordingHttpMessageHandler();
        http.RespondDownloadHeader(
            "https://registry.example.com/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            xTerraformGet);
        http.RespondBytes("https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz", [1, 2, 3], "application/gzip");
        var moduleService = new Mock<IModuleService>();
        moduleService.SetupSequence(x => x.GetModuleDownloadPathAsync("hashicorp", "vpc", "aws", "1.2.3"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("/module/download?token=abc");
        var publish = new Mock<IModulePublishCoordinator>();
        publish.Setup(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return CreateService(http, moduleService: moduleService, publish: publish);
    }

    private static ModuleMirrorService CreateService(
        RecordingHttpMessageHandler http,
        Mock<IModuleMirrorRepository>? repository = null,
        Mock<IModuleService>? moduleService = null,
        Mock<IModulePublishCoordinator>? publish = null,
        Mock<IMirrorPolicyService>? policy = null,
        IMirrorPolicyService? policyService = null,
        Mock<IMirrorLeaseService>? lease = null,
        MirrorOptions? options = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        options ??= new MirrorOptions
        {
            Enabled = true,
            UpstreamRegistryBaseUrl = "https://registry.example.com",
            Modules =
            {
                Enabled = true,
                AllowedArchiveHosts = ["registry.example.com", "github.com"]
            }
        };
        var config = new Mock<IMirrorConfigService>();
        config.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorConfigResponse { Effective = options });

        if (policyService is null && policy is null)
        {
            policy = new Mock<IMirrorPolicyService>();
            policy.Setup(x => x.IsModuleAllowedAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            policy.Setup(x => x.ValidateModuleArchiveUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) =>
                    new ValidatedMirrorEndpoint(new Uri(url), [IPAddress.Parse("93.184.216.34")]));
        }

        lease ??= new Mock<IMirrorLeaseService>();
        lease.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                "module-package",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorLeaseHandle
            {
                Id = Guid.NewGuid(),
                LeaseKey = "module-package",
                OperationType = "module-package",
                OwnerInstanceId = "test",
                ExpiresAt = DateTime.UtcNow.AddMinutes(1)
            });

        var clientFactory = httpClientFactory ?? new SingleClientFactory(new HttpClient(http));
        return new ModuleMirrorService(
            moduleService?.Object ?? Mock.Of<IModuleService>(),
            repository?.Object ?? Mock.Of<IModuleMirrorRepository>(),
            policyService ?? policy!.Object,
            config.Object,
            lease.Object,
            clientFactory,
            new MirrorHttpClient(clientFactory, policyService ?? policy!.Object, NullLogger<MirrorHttpClient>.Instance),
            publish?.Object ?? Mock.Of<IModulePublishCoordinator>(),
            NullLogger<ModuleMirrorService>.Instance);
    }

    private static MirrorPolicyService CreateRealPolicy(IReadOnlyDictionary<string, IPAddress[]> addressesByHost)
    {
        var config = new Mock<IMirrorConfigService>();
        config.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorConfigResponse
            {
                Effective = new MirrorOptions
                {
                    Enabled = true,
                    Modules =
                    {
                        Enabled = true,
                        AllowedArchiveHosts = ["private.example.com", "github.com"]
                    }
                }
            });

        return new MirrorPolicyService(
            config.Object,
            new StubWebhookHostResolver(addressesByHost),
            NullLogger<MirrorPolicyService>.Instance);
    }

    private static ModuleVersions ModuleVersions(params string[] versions) =>
        new()
        {
            Modules =
            [
                new ModuleVersionInfo
                {
                    Versions = versions.Select(version => new VersionInfo { Version = version }).ToList()
                }
            ]
        };

    private static string VersionsJson(params string[] versions) =>
        JsonSerializer.Serialize(new
        {
            modules = new[]
            {
                new
                {
                    versions = versions.Select(version => new { version })
                }
            }
        });

    private static TerraformModule CreateModule(string description, string? sourceKind = null)
    {
        return new TerraformModule
        {
            Id = "hashicorp/vpc/aws/1.2.3",
            Owner = "hashicorp",
            Namespace = "hashicorp",
            Name = "vpc",
            Provider = "aws",
            Version = "1.2.3",
            Description = description,
            Source = "https://github.com/hashicorp/vpc",
            PublishedAt = "2026-01-01T00:00:00Z",
            Versions = ["1.2.3"],
            Root = "",
            Submodules = [],
            Providers = [],
            DownloadUrl = "/v1/modules/hashicorp/vpc/aws/1.2.3/download",
            Metadata = sourceKind is null
                ? null
                : new ModuleArtifactMetadata { Source = new ModuleSourceInfo { Kind = sourceKind } }
        };
    }

    private static TerraformModule CreateMirrorModule() => CreateModule("Mirrored VPC", "mirror");

    private static MirrorModulePackage ReadyModulePackage(string? metadataJson)
    {
        return new MirrorModulePackage
        {
            Hostname = "registry.example.com",
            Namespace = "hashicorp",
            Name = "vpc",
            Provider = "aws",
            Version = "1.2.3",
            DownloadUrl = "https://github.com/hashicorp/vpc/archive/v1.2.3.tar.gz",
            PackageStoragePath = "/module/download?token=cached",
            MetadataJson = metadataJson,
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };
    }

    private static string MirrorMetadata(string? preservedSuffix = null, string? archiveFormat = null) =>
        JsonSerializer.Serialize(new
        {
            preservedSuffix,
            archiveFormat
        });

    private class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.Ordinal);
        public List<Uri> Requests { get; } = [];

        public void RespondJson(string url, string json)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public void RespondBytes(string url, byte[] bytes, string contentType)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            _responses[url].Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        public void RespondDownloadHeader(string url, string xTerraformGet)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Headers = { { "X-Terraform-Get", xTerraformGet } }
            };
        }

        public void RespondRedirect(string url, string location)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri(location) }
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (!_responses.TryGetValue(request.RequestUri!.ToString(), out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(Clone(response));
        }

        private static HttpResponseMessage Clone(HttpResponseMessage response)
        {
            var clone = new HttpResponseMessage(response.StatusCode);
            foreach (var header in response.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (response.Content is null)
            {
                return clone;
            }

            if (response.Content is StringContent)
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType);
            }
            else
            {
                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(body);
                clone.Content.Headers.ContentType = response.Content.Headers.ContentType;
            }

            return clone;
        }
    }

    private sealed class AutoRedirectRecordingHttpMessageHandler : RecordingHttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) ||
                response.Headers.Location is null)
            {
                return response;
            }

            var redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(request.RequestUri!, response.Headers.Location);
            using var redirectRequest = new HttpRequestMessage(HttpMethod.Get, redirectUri);
            return await base.SendAsync(redirectRequest, cancellationToken);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DiscoveryClientFactory(
        HttpClient unnamedClient,
        HttpClient discoveryClient,
        HttpClient mirrorClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return name switch
            {
                "" => unnamedClient,
                "TerraformRegistryMirrorDiscovery" => discoveryClient,
                "TerraformRegistryMirror" => mirrorClient,
                _ => unnamedClient
            };
        }
    }

    private sealed class StubWebhookHostResolver(IReadOnlyDictionary<string, IPAddress[]> addressesByHost) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addressesByHost.TryGetValue(host, out var addresses) ? addresses : [IPAddress.Parse("93.184.216.34")]);
    }
}
