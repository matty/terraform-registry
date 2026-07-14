using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;
using TerraformRegistry.Tests.UnitTests;

namespace TerraformRegistry.Tests.IntegrationTests;

public sealed class OperationalMetricsStartupTests
{
    [Fact]
    public void StartupRegistersSharedOperationalMetricsAndEmitsMirrorAdmissionMeasurements()
    {
        var tempDir = CreateTestDirectory();

        try
        {
            using var listener = new OperationalMetricsTestListener();
            using var baseFactory = new WebApplicationFactory<Program>();
            using var factory = baseFactory
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Test");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["AuthorizationToken"] = "startup-test-auth-token",
                            ["DatabaseProvider"] = "sqlite",
                            ["Sqlite:ConnectionString"] = $"Data Source={ResolveTestPath(tempDir, "metrics-test.db")}",
                            ["StorageProvider"] = "local",
                            ["ModuleStoragePath"] = ResolveTestPath(tempDir, "modules"),
                            ["ProviderStoragePath"] = ResolveTestPath(tempDir, "providers"),
                            ["ModuleExtraction:Enabled"] = "false",
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

            using var client = factory.CreateClient();
            using var scope = factory.Services.CreateScope();
            var services = scope.ServiceProvider;
            var metrics = services.GetRequiredService<OperationalMetrics>();
            var admission = services.GetRequiredService<MirrorDownloadAdmission>();

            Assert.Same(metrics, services.GetRequiredService<OperationalMetrics>());

            using var lease = admission.TryAcquire(new MirrorLimitRuntimeOptions
            {
                MaxConcurrentDownloads = 1,
                MaxConcurrentDownloadsPerCoordinate = 1
            }, "provider:acme/network");

            Assert.NotNull(lease);
            Assert.Contains(listener.Measurements, measurement =>
                measurement.Name == "terraform_registry.mirror.active_downloads");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var tempDir = Path.GetFullPath(Path.Join(Path.GetTempPath(), $"tf-reg-metrics-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string ResolveTestPath(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        if (Path.IsPathFullyQualified(relativePath))
            throw new ArgumentException("Test path segment must be relative.", nameof(relativePath));

        var canonicalRoot = Path.GetFullPath(root);
        var resolvedPath = Path.GetFullPath(Path.Join(canonicalRoot, relativePath));
        var rootPrefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        if (!resolvedPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Test path must remain under its temporary root.", nameof(relativePath));

        return resolvedPath;
    }
}
