using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public class MirrorPolicyServiceTests
{
    [Fact]
    public async Task ProviderPolicyUsesSegmentAwareAllowAndDenyRules()
    {
        var service = CreateService(new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io"],
                Allowlist = ["registry.terraform.io/hashicorp/*"],
                Denylist = ["registry.terraform.io/hashicorp/aws"]
            }
        });

        Assert.True(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "azurerm", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws/extra", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("registry.other.example", "hashicorp", "azurerm", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderPolicyMatchesAllowlistWithHostnameSegment()
    {
        var service = CreateService(new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io", "otherhost"],
                Allowlist = ["registry.terraform.io/hashicorp/*"]
            }
        });

        Assert.True(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("otherhost", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderPolicyMatchesDenylistWithHostnameSegmentWhenAllowlistIsEmpty()
    {
        var service = CreateService(new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io"],
                Denylist = ["registry.terraform.io/hashicorp/*"]
            }
        });

        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderPolicyHostnameWildcardMatchesOneSegmentOnly()
    {
        var service = CreateService(new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io"],
                Allowlist = ["registry.terraform.io/hashicorp/*"]
            }
        });

        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws/extra", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderPolicyAllowsValidCoordinatesWhenAllowlistIsEmptyUnlessDenied()
    {
        var service = CreateService(new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io"],
                Denylist = ["registry.terraform.io/blocked/*"],
                Platforms = ["linux_amd64"]
            }
        });

        Assert.True(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "hashicorp", "aws", "darwin", "arm64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("registry.terraform.io", "blocked", "aws", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderPolicyUsesRuntimeEffectiveConfig()
    {
        var startupOptions = new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["startup.example.com"],
                Allowlist = ["startup.example.com/hashicorp/*"]
            }
        };
        var service = CreateService(startupOptions, effectiveOptions: new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["runtime.example.com"],
                Allowlist = ["runtime.example.com/hashicorp/*"]
            }
        });

        Assert.True(await service.IsProviderAllowedAsync("runtime.example.com", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
        Assert.False(await service.IsProviderAllowedAsync("startup.example.com", "hashicorp", "aws", "linux", "amd64", CancellationToken.None));
    }

    [Fact]
    public async Task ModulePolicyUsesAllowedNamespacesAndSegmentAwareAllowDenyRules()
    {
        var service = CreateService(new MirrorOptions
        {
            Modules = new MirrorModuleRuntimeOptions
            {
                AllowedNamespaces = ["internal"],
                Allowlist = ["internal/*/aws"],
                Denylist = ["internal/network/aws"]
            }
        });

        Assert.True(await service.IsModuleAllowedAsync("registry.terraform.io", "internal", "compute", "aws", CancellationToken.None));
        Assert.False(await service.IsModuleAllowedAsync("registry.terraform.io", "internal", "network", "aws", CancellationToken.None));
        Assert.False(await service.IsModuleAllowedAsync("registry.terraform.io", "internal", "network/extra", "aws", CancellationToken.None));
        Assert.False(await service.IsModuleAllowedAsync("registry.terraform.io", "external", "compute", "aws", CancellationToken.None));
    }

    [Theory]
    [InlineData("http://github.com/acme/module/archive/main.zip")]
    [InlineData("https://user:token@github.com/acme/module/archive/main.zip")]
    [InlineData("file:///tmp/module.zip")]
    [InlineData("ssh://github.com/acme/module.git")]
    [InlineData("git://github.com/acme/module.git")]
    public async Task ValidateModuleArchiveUrlAsyncRejectsUnsafeSchemesAndUserInfo(string url)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateModuleArchiveUrlAsync(url, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://127.0.0.1/module.zip")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.8/module.zip")]
    public async Task ValidateModuleArchiveUrlAsyncRejectsPrivateAndLocalTargets(string url)
    {
        var service = CreateService(addresses: [IPAddress.Loopback]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateModuleArchiveUrlAsync(url, CancellationToken.None));
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("198.51.100.10")]
    [InlineData("203.0.113.10")]
    [InlineData("2001:db8::1")]
    public async Task ValidateModuleArchiveUrlAsyncRejectsReservedDocumentationTargets(string address)
    {
        var service = CreateService(addresses: [IPAddress.Parse(address)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateModuleArchiveUrlAsync("https://github.com/acme/module/archive/main.zip", CancellationToken.None));
    }

    [Fact]
    public async Task ValidateModuleArchiveUrlAsyncAllowsDefaultGithubArchiveHosts()
    {
        var service = CreateService(addresses: [IPAddress.Parse("140.82.112.9")]);

        var endpoint = await service.ValidateModuleArchiveUrlAsync(
            "https://codeload.github.com/acme/module/zip/refs/tags/v1.0.0",
            CancellationToken.None);

        Assert.Equal("codeload.github.com", endpoint.Uri.Host);
        Assert.Collection(endpoint.Addresses, address => Assert.Equal(IPAddress.Parse("140.82.112.9"), address));
    }

    [Fact]
    public async Task ValidateModuleArchiveUrlAsyncRejectsHostsOutsideArchiveAllowlist()
    {
        var service = CreateService(addresses: [IPAddress.Parse("93.184.216.34")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateModuleArchiveUrlAsync("https://example.com/module.zip", CancellationToken.None));
    }

    private static MirrorPolicyService CreateService(
        MirrorOptions? options = null,
        MirrorOptions? effectiveOptions = null,
        IPAddress[]? addresses = null)
    {
        var configService = new Mock<IMirrorConfigService>();
        configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorConfigResponse
            {
                Effective = effectiveOptions ?? options ?? new MirrorOptions(),
                HasRuntimeOverride = effectiveOptions is not null
            });

        return new MirrorPolicyService(
            configService.Object,
            new StubWebhookHostResolver(addresses ?? [IPAddress.Parse("93.184.216.34")]),
            NullLogger<MirrorPolicyService>.Instance);
    }

    private sealed class StubWebhookHostResolver(params IPAddress[] addresses) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }
}
