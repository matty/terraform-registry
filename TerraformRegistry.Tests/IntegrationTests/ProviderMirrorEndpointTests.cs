using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.IntegrationTests;

public sealed class ProviderMirrorEndpointTests : IAsyncLifetime
{
    private const string AuthToken = "mirror-endpoint-auth-token";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"provider-mirror-endpoints-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private FakeProviderMirrorService _mirror = null!;

    [Fact]
    public async Task ProviderIndexRequiresAuthenticationWhenMirrorConfigRequiresIt()
    {
        var response = await _client.GetAsync("/mirror/providers/registry.terraform.io/hashicorp/aws/index.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProviderIndexReturnsNetworkMirrorShape()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await _client.GetAsync("/mirror/providers/registry.terraform.io/hashicorp/aws/index.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var versions = json.GetProperty("versions");
        Assert.True(versions.TryGetProperty("5.0.0", out var version));
        Assert.Equal(JsonValueKind.Object, version.ValueKind);
    }

    [Fact]
    public async Task ProviderIndexAuthenticatedUserWithoutMirrorReadReturnsForbiddenBeforeServiceCall()
    {
        _mirror.IndexCalls = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mirror/providers/registry.terraform.io/hashicorp/aws/index.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "no-mirror-read-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _mirror.IndexCalls);
    }

    [Fact]
    public async Task ProviderVersionAuthenticatedUserWithoutMirrorReadReturnsForbiddenBeforeServiceCall()
    {
        _mirror.VersionCalls = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mirror/providers/registry.terraform.io/hashicorp/aws/5.0.0.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "no-mirror-read-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _mirror.VersionCalls);
    }

    [Fact]
    public async Task ProviderIndexAuthenticatedUserWithMirrorReadSucceeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mirror/providers/registry.terraform.io/hashicorp/aws/index.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "mirror-read-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProviderVersionReturnsSignedArchiveUrlAndTerraformHashes()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await _client.GetAsync("/mirror/providers/registry.terraform.io/hashicorp/aws/5.0.0.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var archive = json.GetProperty("archives").GetProperty("linux_amd64");
        var url = archive.GetProperty("url").GetString();
        var hashes = archive.GetProperty("hashes").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.StartsWith("/mirror/providers/registry.terraform.io/hashicorp/aws/terraform-provider-aws_5.0.0_linux_amd64.zip?", url);
        var hash = Assert.Single(hashes);
        Assert.StartsWith("zh:", hash);
        Assert.DoesNotContain(ExpectedSha256, hashes);
    }

    [Fact]
    public async Task ProviderPackageSignedUrlWorksWithoutTerraformCredential()
    {
        var response = await _client.GetAsync(_mirror.PackageUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ProviderPackageMissingArtifactReturnsNotFound()
    {
        var response = await _client.GetAsync(_mirror.MissingPackageUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownMirrorRouteReturnsProblemJsonNotSpaHtml()
    {
        var response = await _client.GetAsync("/mirror/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Not Found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _mirror = new FakeProviderMirrorService();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DatabaseProvider"] = "sqlite",
                        ["Sqlite:ConnectionString"] = $"Data Source={Path.Combine(_root, "terraform.db")}",
                        ["StorageProvider"] = "local",
                        ["BaseUrl"] = "http://localhost:5000",
                        ["ModuleStoragePath"] = Path.Combine(_root, "modules"),
                        ["ProviderStoragePath"] = Path.Combine(_root, "providers"),
                        ["ModuleExtraction:Enabled"] = "false",
                        ["AuthorizationToken"] = AuthToken,
                        ["Oidc:JwtSecretKey"] = "provider-mirror-endpoint-jwt-secret-key",
                        ["Mirror:Enabled"] = "true",
                        ["Mirror:Providers:Enabled"] = "true",
                        ["Mirror:Providers:RequireAuthentication"] = "true",
                        ["Mirror:PackageUrlSigningKey"] = "provider-mirror-endpoint-signing-key"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<OidcOptions>();
                    services.AddSingleton(new OidcOptions
                    {
                        JwtSecretKey = "provider-mirror-endpoint-jwt-secret-key",
                        JwtExpiryHours = 24
                    });
                    services.RemoveAll<IProviderMirrorService>();
                    services.AddSingleton<IProviderMirrorService>(_mirror);
                    services.RemoveAll<IApiKeyService>();
                    services.AddScoped<IApiKeyService, FakeApiKeyService>();
                    services.RemoveAll<IPermissionService>();
                    services.AddSingleton<IPermissionService, FakePermissionService>();
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private const string ExpectedSha256 = "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81";

    private sealed class FakeProviderMirrorService : IProviderMirrorService
    {
        public int IndexCalls { get; set; }
        public int VersionCalls { get; set; }

        public string PackageUrl { get; } =
            "/mirror/providers/registry.terraform.io/hashicorp/aws/terraform-provider-aws_5.0.0_linux_amd64.zip?version=5.0.0&os=linux&arch=amd64&expires=4070908800&signature=valid";

        public string MissingPackageUrl { get; } =
            "/mirror/providers/registry.terraform.io/hashicorp/aws/missing.zip?version=5.0.0&os=linux&arch=amd64&expires=4070908800&signature=missing";

        public Task<ProviderMirrorIndexResponse?> GetProviderIndexAsync(
            string hostname,
            string providerNamespace,
            string type,
            CancellationToken cancellationToken)
        {
            IndexCalls++;
            return Task.FromResult<ProviderMirrorIndexResponse?>(new ProviderMirrorIndexResponse
            {
                Versions = new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["5.0.0"] = new()
                }
            });
        }

        public Task<ProviderMirrorVersionResponse?> GetProviderVersionAsync(
            string hostname,
            string providerNamespace,
            string type,
            string version,
            CancellationToken cancellationToken)
        {
            VersionCalls++;
            return Task.FromResult<ProviderMirrorVersionResponse?>(new ProviderMirrorVersionResponse
            {
                Archives = new Dictionary<string, ProviderMirrorArchive>(StringComparer.Ordinal)
                {
                    ["linux_amd64"] = new ProviderMirrorArchive
                    {
                        Url = PackageUrl,
                        Hashes = [$"zh:{ExpectedSha256}"]
                    }
                }
            });
        }

        public Task<ProviderMirrorPackageDownload?> OpenPackageAsync(
            string hostname,
            string providerNamespace,
            string type,
            string filename,
            IReadOnlyDictionary<string, string[]> query,
            CancellationToken cancellationToken)
        {
            if (query.TryGetValue("signature", out var signature) && signature is ["missing"])
            {
                return Task.FromResult<ProviderMirrorPackageDownload?>(null);
            }

            return Task.FromResult<ProviderMirrorPackageDownload?>(new ProviderMirrorPackageDownload(
                new MemoryStream([1, 2, 3]),
                "terraform-provider-aws_5.0.0_linux_amd64.zip",
                "application/zip",
            3));
        }
    }

    private sealed class FakeApiKeyService : IApiKeyService
    {
        public Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken)
        {
            return rawToken switch
            {
                "no-mirror-read-token" => Task.FromResult(new ApiKeyValidationResult(new ApiKey
                {
                    UserId = "no-mirror-read-user",
                    Description = "test",
                    TokenHash = "hash",
                    Prefix = "no-mirror"
                }, false)),
                "mirror-read-token" => Task.FromResult(new ApiKeyValidationResult(new ApiKey
                {
                    UserId = "mirror-read-user",
                    Description = "test",
                    TokenHash = "hash",
                    Prefix = "mirror"
                }, false)),
                _ => Task.FromResult(new ApiKeyValidationResult(null, false))
            };
        }

        public Task<(string RawToken, ApiKey Key)> CreateApiKeyAsync(string userId, string description, bool isShared = false) =>
            throw new NotSupportedException();

        public Task<(string RawToken, ApiKey Key)> CreateExpiringApiKeyAsync(string userId, string description, DateTime expiresAt, bool isShared = false) =>
            throw new NotSupportedException();

        public Task<ApiKey?> GetApiKeyAsync(Guid id) => throw new NotSupportedException();
        public Task<IEnumerable<ApiKey>> ListApiKeysAsync(string userId) => throw new NotSupportedException();
        public Task<IEnumerable<ApiKey>> ListSharedApiKeysAsync() => throw new NotSupportedException();
        public Task<bool> RevokeApiKeyAsync(Guid keyId, string userId) => throw new NotSupportedException();
        public Task<ApiKeyUpdateResult> UpdateApiKeyAsync(Guid keyId, string requestingUserId, string description, bool isShared) => throw new NotSupportedException();
        public Task<User> GetOrCreateOidcUserAsync(string email, string provider, string providerId) => throw new NotSupportedException();
        public Task<User> GetOrCreateUserAsync(string email, string provider, string providerId) => throw new NotSupportedException();
        public Task<User?> GetUserByIdAsync(string id) => throw new NotSupportedException();
    }

    private sealed class FakePermissionService : IPermissionService
    {
        public Task<string[]> GetUserPermissionsAsync(string userId)
        {
            return Task.FromResult(userId == "mirror-read-user" ? [Permissions.MirrorRead] : Array.Empty<string>());
        }

        public Task EnsureDefaultRoleAsync(string userId) => Task.CompletedTask;
        public Task<IEnumerable<Role>> GetUserRolesAsync(string userId) => throw new NotSupportedException();
        public Task<bool> AssignRoleAsync(string userId, Guid roleId, string? assignedBy) => throw new NotSupportedException();
        public Task<bool> RemoveRoleAsync(string userId, Guid roleId) => throw new NotSupportedException();
        public Task<IEnumerable<string>> GetUsersWithRoleAsync(Guid roleId) => throw new NotSupportedException();
    }
}
