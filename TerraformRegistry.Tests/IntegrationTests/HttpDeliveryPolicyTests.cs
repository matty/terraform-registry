using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.IntegrationTests;

public sealed class HttpDeliveryPolicyTests : IDisposable
{
    private readonly string _tempDirectory = Path.GetTempFileName();
    private readonly WebApplicationFactory<Program> _factory;

    public HttpDeliveryPolicyTests()
    {
        File.Delete(_tempDirectory);
        Directory.CreateDirectory(_tempDirectory);
        _factory = new DeliveryPolicyFactory(_tempDirectory);
    }

    private sealed class DeliveryPolicyFactory(string tempDirectory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AuthorizationToken"] = "http-delivery-test-token",
                    ["DatabaseProvider"] = "sqlite",
                    ["Sqlite:ConnectionString"] = $"Data Source={Path.Join(tempDirectory, "registry.db")}",
                    ["StorageProvider"] = "local",
                    ["ModuleStoragePath"] = Path.Join(tempDirectory, "modules"),
                    ["ProviderStoragePath"] = Path.Join(tempDirectory, "providers"),
                    ["Oidc:JwtSecretKey"] = "http-delivery-test-jwt-secret-key-32-chars-minimum"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<OidcOptions>();
                services.AddSingleton(new OidcOptions
                {
                    JwtSecretKey = "http-delivery-test-jwt-secret-key-32-chars-minimum",
                    JwtExpiryHours = 24
                });
            });
        }
    }

    [Fact]
    public async Task FingerprintedFrontendAssetIsCompressedAndCachedImmutably()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip");

        using var response = await client.GetAsync("/_nuxt/8_MDK3Q6.js", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains(response.Headers.CacheControl?.Extensions ?? [], extension =>
            string.Equals(extension.Name, "immutable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApiResponsesAreNeverCacheable()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/v1/modules");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FingerprintedFrontendAssetSupportsBrotli()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "br");

        using var response = await client.GetAsync("/_nuxt/8_MDK3Q6.js", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
