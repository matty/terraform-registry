using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.IntegrationTests;

/// <summary>
/// Upload endpoints advertise limits far above Kestrel's 30,000,000 byte default
/// (100 MiB for module archives, 512 MiB for provider packages). Without an explicit
/// per-endpoint limit the server rejects anything over ~28.6 MiB with a bare 413 long
/// before the handler can apply - or report - the configured limit, so the documented
/// limits are unreachable. These tests pin the endpoint limits to the configured values.
/// </summary>
public class UploadRequestLimitTests
{
    // Deliberately non-default so a limit that silently falls back to a default-constructed
    // options object (rather than the configured one) is caught.
    private const long ConfiguredArchiveBytes = 111_222_333;
    private const long ConfiguredPackageBytes = 444_555_666;

    private static WebApplicationFactory<Program> CreateFactory(string tempDir) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AuthorizationToken"] = "upload-limit-test-token",
                        ["DatabaseProvider"] = "sqlite",
                        ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(tempDir, "upload-limits.db")}",
                        ["StorageProvider"] = "local",
                        ["ModuleStoragePath"] = Path.Combine(tempDir, "modules"),
                        ["Oidc:JwtSecretKey"] = "upload-limit-test-jwt-secret-key-32-chars",
                        ["ModuleExtraction:MaxArchiveBytes"] =
                            ConfiguredArchiveBytes.ToString(CultureInfo.InvariantCulture),
                        ["ProviderUpload:MaxPackageBytes"] =
                            ConfiguredPackageBytes.ToString(CultureInfo.InvariantCulture)
                    });
                });
                builder.ConfigureServices(services =>
                {
                    // ProviderUploadOptions is bound eagerly during Program.cs service
                    // registration, which happens before WebApplicationFactory's
                    // ConfigureAppConfiguration is applied, so the singleton has to be
                    // replaced rather than configured. Replacing it also proves the endpoint
                    // reads the registered instance and not a default-constructed one.
                    services.RemoveAll<ProviderUploadOptions>();
                    services.AddSingleton(new ProviderUploadOptions
                    {
                        MaxPackageBytes = ConfiguredPackageBytes
                    });
                    services.RemoveAll<OidcOptions>();
                    services.AddSingleton(new OidcOptions
                    {
                        JwtSecretKey = "upload-limit-test-jwt-secret-key-32-chars",
                        JwtExpiryHours = 24
                    });
                });
            });

    private static long? RequestSizeLimitFor(
        WebApplicationFactory<Program> factory, string routePattern, string httpMethod)
    {
        // Several verbs share these route patterns, so the method has to be part of the match.
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var endpoint = endpoints.OfType<RouteEndpoint>()
            .FirstOrDefault(e =>
                string.Equals(e.RoutePattern.RawText, routePattern, StringComparison.OrdinalIgnoreCase) &&
                e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    .Contains(httpMethod, StringComparer.OrdinalIgnoreCase) == true);

        Assert.NotNull(endpoint);
        return endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
    }

    [Fact]
    public void ModuleUploadEndpointAllowsTheConfiguredArchiveLimit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = CreateFactory(tempDir);
            _ = factory.CreateClient();

            var limit = RequestSizeLimitFor(factory, "/v1/modules/{namespace}/{name}/{provider}/{version}", "POST");

            // Tracks the configured value, not a default, and clears Kestrel's 30,000,000
            // byte default so the documented limit is actually reachable.
            Assert.Equal(UploadRequestLimits.ForUploadOf(ConfiguredArchiveBytes), limit);
            Assert.True(limit > 30_000_000,
                "module upload limit must exceed Kestrel's 30,000,000 byte default to be reachable");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProviderPlatformUploadEndpointAllowsTheConfiguredPackageLimit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = CreateFactory(tempDir);
            _ = factory.CreateClient();

            var limit = RequestSizeLimitFor(factory,
                "/api/providers/{namespace}/{type}/versions/{version}/platforms", "POST");

            Assert.Equal(UploadRequestLimits.ForUploadOf(ConfiguredPackageBytes), limit);
            Assert.NotEqual(UploadRequestLimits.ForUploadOf(new ProviderUploadOptions().MaxPackageBytes), limit);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void OrdinaryEndpointsKeepTheServerDefaultLimit()
    {
        // The raised limits must be scoped to the upload routes, not applied globally.
        var tempDir = Path.Combine(Path.GetTempPath(), $"tf-reg-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var factory = CreateFactory(tempDir);
            _ = factory.CreateClient();

            Assert.Null(RequestSizeLimitFor(factory, "/api/auth/providers", "GET"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
