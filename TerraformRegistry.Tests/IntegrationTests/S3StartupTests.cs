using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.IntegrationTests;

public class S3StartupTests
{
    [Fact]
    public void StartupWithS3ProviderResolvesProviderArtifactStorage()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"tf-reg-s3-provider-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var s3Client = new Mock<IAmazonS3>();
            s3Client.Setup(client => client.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ListObjectsV2Response());

            var s3ClientFactory = new Mock<IS3ClientFactory>();
            s3ClientFactory.Setup(factory => factory.Create(
                    It.IsAny<AmazonS3Config>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(s3Client.Object);

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
                            ["S3:BucketName"] = "registry-artifacts",
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
                        services.RemoveAll<IS3ClientFactory>();
                        services.AddSingleton(s3ClientFactory.Object);
                    });
                });

            using var client = configuredFactory.CreateClient();
            using var scope = configuredFactory.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IProviderArtifactStorage>();

            Assert.IsType<S3ProviderArtifactStorage>(storage);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void StartupWithS3ProviderAndMissingBucketNameThrowsDuringCreateClient()
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
