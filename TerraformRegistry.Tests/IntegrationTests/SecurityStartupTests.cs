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
    public void ProductionStartup_WithDefaultAuthorizationToken_Throws()
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
    public void ProductionStartup_WithPlaceholderJwtSecret_Throws()
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

}
