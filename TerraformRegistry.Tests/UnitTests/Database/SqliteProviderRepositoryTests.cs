using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests.Database;

public class SqliteProviderRepositoryTests
{
    [Fact]
    public async Task CreateProviderAsync_PersistsProviderAndReturnsCreatedRecord()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        var repository = fixture.Repository;

        var provider = await repository.CreateProviderAsync(new TerraformProvider
        {
            Namespace = "acme",
            Type = "example",
            DisplayName = "Example",
            Description = "Example provider",
            CreatedBy = "user-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var loaded = await repository.GetProviderAsync("acme", "example");

        Assert.Equal(provider.Id, loaded?.Id);
        Assert.Equal("acme", loaded?.Namespace);
        Assert.Equal("example", loaded?.Type);
        Assert.Equal("Example provider", loaded?.Description);
    }

    [Fact]
    public async Task GetProviderVersionsAsync_ReturnsPlatformsForActiveVersionsOnly()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        var provider = await fixture.SeedProviderWithVersionAndPlatformAsync();
        await fixture.SeedDeletedVersionAsync(provider.Id);

        var versions = await fixture.Repository.GetProviderVersionsAsync(provider.Namespace, provider.Type);

        var version = Assert.Single(versions);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(["5.0"], version.Protocols);
        var platform = Assert.Single(version.Platforms);
        Assert.Equal("linux", platform.Os);
        Assert.Equal("amd64", platform.Arch);
    }

    [Fact]
    public async Task GetProviderVersionsAsync_HidesVersionsAndPlatformsUntilArtifactsAreInstallable()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        var provider = await fixture.SeedProviderWithVersionAndPlatformAsync();
        await fixture.SeedIncompleteVersionAsync(provider.Id, "1.1.0");
        await fixture.SeedVersionWithUnuploadedPlatformAsync(provider.Id, "1.2.0");
        await fixture.SeedUnuploadedPlatformAsync(provider.Id, "1.0.0");

        var versions = await fixture.Repository.GetProviderVersionsAsync(provider.Namespace, provider.Type);

        var version = Assert.Single(versions);
        Assert.Equal("1.0.0", version.Version);
        var platform = Assert.Single(version.Platforms);
        Assert.Equal("linux", platform.Os);
        Assert.Equal("amd64", platform.Arch);
    }

    [Fact]
    public async Task GetProviderArtifactStoragePathsAsync_ReturnsVersionAndPlatformArtifacts()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        await fixture.SeedProviderWithVersionAndPlatformAsync();

        var paths = await fixture.Repository.GetProviderArtifactStoragePathsAsync("acme", "example", "1.0.0", null, null);

        Assert.Equal(
            [
                "providers/acme/example/1.0.0/SHA256SUMS",
                "providers/acme/example/1.0.0/SHA256SUMS.sig",
                "providers/acme/example/1.0.0/linux_amd64.zip"
            ],
            paths.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ProviderGpgKeyIsReferencedByActiveVersionsAsync_IgnoresDeletedVersions()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        var provider = await fixture.SeedProviderWithVersionAndPlatformAsync();

        Assert.True(await fixture.Repository.ProviderGpgKeyIsReferencedByActiveVersionsAsync("acme", "ABC123"));

        Assert.True(await fixture.Repository.DeleteProviderVersionAsync(provider.Namespace, provider.Type, "1.0.0"));
        Assert.False(await fixture.Repository.ProviderGpgKeyIsReferencedByActiveVersionsAsync("acme", "ABC123"));
    }

    [Fact]
    public async Task GetProviderPlatformAsync_ReturnsPackageMetadata()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        await fixture.SeedProviderWithVersionAndPlatformAsync();

        var platform = await fixture.Repository.GetProviderPlatformAsync("acme", "example", "1.0.0", "linux", "amd64");

        Assert.NotNull(platform);
        Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip", platform!.Filename);
        Assert.Equal(new string('a', 64), platform.Shasum);
    }

    [Fact]
    public async Task AddGpgKeyAsync_PersistsNamespaceKeyAndLookupIgnoresRevokedKeys()
    {
        using var fixture = await SqliteProviderRepositoryFixture.CreateAsync();
        var repository = fixture.Repository;

        await repository.AddGpgKeyAsync(new ProviderGpgKey
        {
            Namespace = "acme",
            KeyId = "ABC123",
            AsciiArmor = "public-key",
            Source = "manual",
            CreatedAt = DateTime.UtcNow
        });

        var loaded = await repository.GetGpgKeyAsync("acme", "ABC123");
        Assert.NotNull(loaded);
        Assert.Equal("public-key", loaded!.AsciiArmor);

        Assert.True(await repository.RevokeGpgKeyAsync("acme", "ABC123"));
        Assert.Null(await repository.GetGpgKeyAsync("acme", "ABC123"));
    }

    private sealed class SqliteProviderRepositoryFixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        public SqliteProviderRepository Repository { get; }

        private SqliteProviderRepositoryFixture(SqliteConnection connection, string connectionString)
        {
            _connection = connection;
            Repository = new SqliteProviderRepository(connectionString);
        }

        public static async Task<SqliteProviderRepositoryFixture> CreateAsync()
        {
            var dbName = $"ProviderRepoTest_{Guid.NewGuid():N}";
            var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var migrator = new DbUpMigrator(NullLogger<DbUpMigrator>.Instance);
            migrator.Migrate("sqlite", connectionString);

            return new SqliteProviderRepositoryFixture(connection, connectionString);
        }

        public async Task<TerraformProvider> SeedProviderWithVersionAndPlatformAsync()
        {
            var provider = await Repository.CreateProviderAsync(new TerraformProvider
            {
                Namespace = "acme",
                Type = "example",
                DisplayName = "Example",
                Description = "Example provider",
                CreatedBy = "user-1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            var version = await Repository.CreateProviderVersionAsync(provider.Id, "1.0.0", ["5.0"], "ABC123");
            await Repository.SetVersionShasumsPathAsync(version.Id, "providers/acme/example/1.0.0/SHA256SUMS");
            await Repository.SetVersionShasumsSignaturePathAsync(version.Id, "providers/acme/example/1.0.0/SHA256SUMS.sig");

            var platform = await Repository.CreateProviderPlatformAsync(
                version.Id,
                "linux",
                "amd64",
                "terraform-provider-example_1.0.0_linux_amd64.zip",
                new string('a', 64));
            await Repository.SetPlatformPackagePathAsync(platform.Id, "providers/acme/example/1.0.0/linux_amd64.zip", 42);

            return provider;
        }

        public async Task SeedDeletedVersionAsync(Guid providerId)
        {
            await Repository.CreateProviderVersionAsync(providerId, "2.0.0", ["5.0"], "ABC123");
            Assert.True(await Repository.DeleteProviderVersionAsync("acme", "example", "2.0.0"));
        }

        public async Task SeedIncompleteVersionAsync(Guid providerId, string versionNumber)
        {
            await Repository.CreateProviderVersionAsync(providerId, versionNumber, ["5.0"], "ABC123");
        }

        public async Task SeedVersionWithUnuploadedPlatformAsync(Guid providerId, string versionNumber)
        {
            var version = await Repository.CreateProviderVersionAsync(providerId, versionNumber, ["5.0"], "ABC123");
            await Repository.SetVersionShasumsPathAsync(version.Id, $"providers/acme/example/{versionNumber}/SHA256SUMS");
            await Repository.SetVersionShasumsSignaturePathAsync(version.Id, $"providers/acme/example/{versionNumber}/SHA256SUMS.sig");
            await Repository.CreateProviderPlatformAsync(
                version.Id,
                "linux",
                "amd64",
                $"terraform-provider-example_{versionNumber}_linux_amd64.zip",
                new string('b', 64));
        }

        public async Task SeedUnuploadedPlatformAsync(Guid providerId, string versionNumber)
        {
            var version = await Repository.GetProviderVersionAsync("acme", "example", versionNumber);
            Assert.NotNull(version);
            await Repository.CreateProviderPlatformAsync(
                version!.Id,
                "darwin",
                "arm64",
                $"terraform-provider-example_{versionNumber}_darwin_arm64.zip",
                new string('c', 64));
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
