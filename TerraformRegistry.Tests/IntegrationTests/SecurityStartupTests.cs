using TerraformRegistry.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.IntegrationTests;

public class SecurityStartupTests
{
    [Fact]
    public void ProductionStartupWithDefaultAuthorizationTokenThrows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "default-auth-token",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(tempDir, "startup-test.db")}",
                            ["StorageProvider"] = "local",
                            ["ModuleStoragePath"] = Path.Combine(tempDir, "modules"),
                            ["Oidc:JwtSecretKey"] = "startup-test-jwt-secret-key-32-chars-minimum"
                        });
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<OidcOptions>();
                        services.AddSingleton(new OidcOptions
                        {
                            JwtSecretKey = "startup-test-jwt-secret-key-32-chars-minimum",
                            JwtExpiryHours = 24
                        });
                    });
                });

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("AuthorizationToken", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProductionStartupWithPlaceholderJwtSecretThrows()
    {
        const string placeholderJwtSecretKey = "your-256-bit-secret-key-here-minimum-32-chars";
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "startup-test-auth-token",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(tempDir, "startup-test.db")}",
                            ["StorageProvider"] = "local",
                            ["ModuleStoragePath"] = Path.Combine(tempDir, "modules")
                        });
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<OidcOptions>();
                        services.AddSingleton(new OidcOptions
                        {
                            JwtSecretKey = placeholderJwtSecretKey,
                            JwtExpiryHours = 24
                        });
                    });
                });

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task LoginCookiesAreSecureOutsideDevelopmentEvenOverPlainHttp(string environmentName)
    {
        // A TLS-terminating proxy forwards plain HTTP to the app, so Request.IsHttps is
        // false for a request that reached the user over HTTPS. Keying the Secure flag off
        // IsHttps therefore silently drops it and the state/return-to cookies travel in the
        // clear. Outside Development the flag must be set regardless of the inbound scheme.
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-cookie-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(environmentName);
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "cookie-test-auth-token",
                            ["ApiKeySecurity:DigestKey"] = "cookie-test-api-key-digest-key-32-chars-min",
                            ["ArtifactDownloadTokens:SigningKey"] = "cookie-test-artifact-signing-key-32-chars",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(tempDir, "cookie-test.db")}",
                            ["StorageProvider"] = "local",
                            ["ModuleStoragePath"] = Path.Combine(tempDir, "modules"),
                            ["Oidc:JwtSecretKey"] = "cookie-test-jwt-secret-key-32-chars-minimum"
                        });
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<ApiKeySecurityOptions>();
                        services.AddSingleton(new ApiKeySecurityOptions
                        {
                            DigestKey = "cookie-test-api-key-digest-key-32-chars-min"
                        });
                        services.RemoveAll<OidcOptions>();
                        services.AddSingleton(new OidcOptions
                        {
                            JwtSecretKey = "cookie-test-jwt-secret-key-32-chars-minimum",
                            JwtExpiryHours = 24,
                            Providers = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["github"] = new()
                                {
                                    Enabled = true,
                                    ClientId = "cookie-test-client",
                                    ClientSecret = "cookie-test-secret",
                                    AuthorizationEndpoint = "https://example.invalid/authorize",
                                    TokenEndpoint = "https://example.invalid/token",
                                    UserInfoEndpoint = "https://example.invalid/userinfo",
                                    Scopes = ["openid", "email"]
                                }
                            }
                        });
                    });
                });

            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost")
            });

            using var response = await client.GetAsync("/api/auth/login/github?returnTo=%2Fmodules");

            Assert.False(response.Headers.TryGetValues("Set-Cookie", out var _unused) is false,
                "login should issue state/return-to cookies");
            var cookies = response.Headers.GetValues("Set-Cookie").ToList();
            Assert.NotEmpty(cookies);

            foreach (var cookie in cookies)
            {
                Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoginCookiesStaySecurelessInDevelopmentSoLocalHttpLoginWorks()
    {
        // Development is the deliberate exception: a Secure cookie would never be sent back
        // over http://localhost, breaking local sign-in.
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-cookie-dev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "cookie-test-auth-token",
                            ["ApiKeySecurity:DigestKey"] = "cookie-test-api-key-digest-key-32-chars-min",
                            ["ArtifactDownloadTokens:SigningKey"] = "cookie-test-artifact-signing-key-32-chars",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(tempDir, "cookie-dev.db")}",
                            ["StorageProvider"] = "local",
                            ["ModuleStoragePath"] = Path.Combine(tempDir, "modules"),
                            ["Oidc:JwtSecretKey"] = "cookie-test-jwt-secret-key-32-chars-minimum"
                        });
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<ApiKeySecurityOptions>();
                        services.AddSingleton(new ApiKeySecurityOptions
                        {
                            DigestKey = "cookie-test-api-key-digest-key-32-chars-min"
                        });
                        services.RemoveAll<OidcOptions>();
                        services.AddSingleton(new OidcOptions
                        {
                            JwtSecretKey = "cookie-test-jwt-secret-key-32-chars-minimum",
                            JwtExpiryHours = 24,
                            Providers = new Dictionary<string, OidcProviderOptions>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["github"] = new()
                                {
                                    Enabled = true,
                                    ClientId = "cookie-test-client",
                                    ClientSecret = "cookie-test-secret",
                                    AuthorizationEndpoint = "https://example.invalid/authorize",
                                    TokenEndpoint = "https://example.invalid/token",
                                    UserInfoEndpoint = "https://example.invalid/userinfo",
                                    Scopes = ["openid", "email"]
                                }
                            }
                        });
                    });
                });

            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost")
            });

            using var response = await client.GetAsync("/api/auth/login/github");
            var cookies = response.Headers.GetValues("Set-Cookie").ToList();

            Assert.NotEmpty(cookies);
            foreach (var cookie in cookies)
            {
                Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
