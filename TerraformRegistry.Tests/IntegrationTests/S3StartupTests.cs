using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.IntegrationTests;

public class S3StartupTests
{
    [Fact]
    public void Startup_WithS3Provider_AndMissingBucketName_Throws_During_CreateClient()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"tf-reg-s3-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var configuredFactory = factory
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "startup-test-auth-token",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={Path.Join(tempDir, "startup-test.db")}",
                            ["StorageProvider"] = "s3",
                            ["S3:Region"] = "eu-west-2",
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

            var ex = Assert.ThrowsAny<Exception>(() => configuredFactory.CreateClient());
            Assert.Contains("S3:BucketName", ex.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
