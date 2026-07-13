using Microsoft.Extensions.Configuration;
using Moq;
using System.Text.Json;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public class MirrorConfigServiceTests
{
    [Fact]
    public void MirrorConfigurationResponseDoesNotSerializeSigningSecrets()
    {
        var response = new MirrorConfigResponse
        {
            Effective = new MirrorOptions
            {
                PackageUrlSigningKey = "private-signing-key",
                Providers = new MirrorProviderRuntimeOptions { TrustedSigningKeyIds = ["trusted-key-id"] }
            }
        };

        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("private-signing-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("trusted-key-id", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConfigAsyncUsesStartupDefaultsWhenRuntimeSettingMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Mirror:Enabled"] = "true",
                ["Mirror:UpstreamRegistryBaseUrl"] = "https://registry.example.com",
                ["Mirror:Providers:Enabled"] = "true",
                ["Mirror:Providers:RequireAuthentication"] = "true",
                ["Mirror:Providers:AllowedHostnames:0"] = "registry.example.com",
                ["Mirror:Modules:AllowedArchiveHosts:0"] = "github.com",
                ["Mirror:Limits:MaxConcurrentDownloads"] = "4"
            })
            .Build();
        var settings = new Mock<IRuntimeSettingsService>();
        settings.Setup(x => x.GetAsync("mirror.config", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        var service = new MirrorConfigService(configuration, settings.Object);

        var result = await service.GetConfigAsync(CancellationToken.None);

        Assert.True(result.Effective.Enabled);
        Assert.Equal("https://registry.example.com", result.Effective.UpstreamRegistryBaseUrl);
        Assert.True(result.Effective.Providers.Enabled);
        Assert.True(result.Effective.Providers.RequireAuthentication);
        Assert.Contains("registry.example.com", result.Effective.Providers.AllowedHostnames);
        Assert.Contains("github.com", result.Effective.Modules.AllowedArchiveHosts);
        Assert.Equal(4, result.Effective.Limits.MaxConcurrentDownloads);
        Assert.False(result.HasRuntimeOverride);
        Assert.Null(result.UpdatedAt);
        Assert.Null(result.UpdatedBy);
    }

    [Fact]
    public async Task GetConfigAsyncMergesRuntimeOperatorControlsOverStartupDefaults()
    {
        var updatedAt = new DateTime(2026, 06, 23, 10, 30, 00, DateTimeKind.Utc);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Mirror:Enabled"] = "false",
                ["Mirror:UpstreamRegistryBaseUrl"] = "https://startup.example.com",
                ["Mirror:Providers:AllowedHostnames:0"] = "startup.example.com",
                ["Mirror:Modules:AllowedArchiveHosts:0"] = "github.com",
                ["Mirror:Limits:MaxConcurrentDownloads"] = "8"
            })
            .Build();
        var settings = new Mock<IRuntimeSettingsService>();
        settings.Setup(x => x.GetAsync("mirror.config", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuntimeSetting
            {
                Key = "mirror.config",
                ValueJson = """
                            {
                              "enabled": true,
                              "providers": {
                                "enabled": true,
                                "requireAuthentication": false,
                                "allowedHostnames": ["registry.terraform.io"],
                                "upstreamRegistryUrls": {
                                  "registry.terraform.io": "https://registry.terraform.io"
                                },
                                "allowlist": ["hashicorp/aws"],
                                "denylist": ["blocked/provider"],
                                "platforms": ["linux_amd64"],
                                "maxPackageBytes": 1024,
                                "metadataTtlMinutes": 15,
                                "downloadTimeoutSeconds": 30
                              },
                              "modules": {
                                "enabled": false,
                                "requireAuthentication": true,
                                "allowedNamespaces": ["internal"],
                                "allowedArchiveHosts": ["codeload.github.com"],
                                "allowlist": ["internal/network/aws"],
                                "denylist": ["blocked/module/aws"],
                                "maxPackageBytes": 2048,
                                "maxRedirects": 2,
                                "metadataTtlMinutes": 20,
                                "downloadTimeoutSeconds": 45
                              },
                              "limits": {
                                "maxConcurrentDownloads": 2,
                                "maxConcurrentDownloadsPerCoordinate": 1,
                                "maxTotalCachedBytes": 4096,
                                "negativeCacheTtlSeconds": 10
                              }
                            }
                            """,
                UpdatedAt = updatedAt,
                UpdatedBy = "admin-user"
            });

        var service = new MirrorConfigService(configuration, settings.Object);

        var result = await service.GetConfigAsync(CancellationToken.None);

        Assert.True(result.Effective.Enabled);
        Assert.Equal("https://startup.example.com", result.Effective.UpstreamRegistryBaseUrl);
        Assert.False(result.Effective.Providers.RequireAuthentication);
        Assert.Contains("linux_amd64", result.Effective.Providers.Platforms);
        Assert.Contains("hashicorp/aws", result.Effective.Providers.Allowlist);
        Assert.False(result.Effective.Modules.Enabled);
        Assert.Contains("codeload.github.com", result.Effective.Modules.AllowedArchiveHosts);
        Assert.Equal(2, result.Effective.Limits.MaxConcurrentDownloads);
        Assert.True(result.HasRuntimeOverride);
        Assert.Equal(updatedAt, result.UpdatedAt);
        Assert.Equal("admin-user", result.UpdatedBy);
    }

    [Fact]
    public async Task UpdateConfigAsyncPersistsOperatorControls()
    {
        var configuration = new ConfigurationBuilder().Build();
        var settings = new Mock<IRuntimeSettingsService>();
        settings.Setup(x => x.GetAsync("mirror.config", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuntimeSetting?)null);
        var service = new MirrorConfigService(configuration, settings.Object);

        var request = new MirrorConfigUpdateRequest
        {
            Enabled = true,
            Providers = new MirrorProviderRuntimeOptions { Enabled = true, Platforms = ["linux_amd64"] },
            Modules = new MirrorModuleRuntimeOptions { Enabled = false, AllowedArchiveHosts = ["codeload.github.com"] },
            Limits = new MirrorLimitRuntimeOptions { MaxConcurrentDownloads = 2 }
        };

        await service.UpdateConfigAsync(request, "admin-user", CancellationToken.None);

        settings.Verify(x => x.SetAsync(
            "mirror.config",
            It.Is<string>(json => json.Contains("linux_amd64") && json.Contains("codeload.github.com")),
            "admin-user",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateConfigAsyncPreservesTrustedSigningKeyIdsThatAreNotExposedToOperators()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Mirror:Providers:TrustedSigningKeyIds:0"] = "trusted-key-id"
            })
            .Build();
        var settings = new Mock<IRuntimeSettingsService>();
        settings.Setup(x => x.GetAsync("mirror.config", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuntimeSetting?)null);
        var service = new MirrorConfigService(configuration, settings.Object);

        await service.UpdateConfigAsync(new MirrorConfigUpdateRequest { Enabled = true }, "operator-1", CancellationToken.None);

        settings.Verify(x => x.SetAsync("mirror.config",
            It.Is<string>(json => json.Contains("trusted-key-id", StringComparison.Ordinal)),
            "operator-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MirrorPermissionsAreAvailableToAdminsButNotDefaultUsers()
    {
        Assert.Contains("mirror.read", Permissions.All);
        Assert.Contains("mirror.manage", Permissions.All);
        Assert.Contains("mirror.configure", Permissions.All);

        Assert.DoesNotContain("mirror.read", Permissions.DefaultUserPermissions);
        Assert.DoesNotContain("mirror.manage", Permissions.DefaultUserPermissions);
        Assert.DoesNotContain("mirror.configure", Permissions.DefaultUserPermissions);
    }

    [Fact]
    public void MirrorConfigurationMapsEveryAllowedProviderHostToItsOwnHttpsUpstream()
    {
        var options = new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["registry.terraform.io", "registry.example.com"],
                UpstreamRegistryUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["registry.terraform.io"] = "https://registry.terraform.io",
                    ["registry.example.com"] = "https://mirror-upstream.example.net"
                }
            }
        };

        MirrorConfigurationValidator.Validate(options);

        Assert.Equal(
            "https://mirror-upstream.example.net/",
            MirrorConfigurationValidator.GetProviderUpstreamUri(options, "registry.example.com").ToString());
    }

    [Fact]
    public void MirrorConfigurationRejectsAnAllowedProviderHostWithoutAnExplicitMapping()
    {
        var options = new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions
            {
                AllowedHostnames = ["first.example.com", "second.example.com"]
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => MirrorConfigurationValidator.Validate(options));

        Assert.Contains("explicit upstream mapping", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MirrorConfigurationRejectsAnEmptyProviderHostnameAllowlist()
    {
        var options = new MirrorOptions
        {
            Providers = new MirrorProviderRuntimeOptions { AllowedHostnames = [] }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => MirrorConfigurationValidator.Validate(options));

        Assert.Contains("at least one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
