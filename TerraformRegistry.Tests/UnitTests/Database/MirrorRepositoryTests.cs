using DotNet.Testcontainers.Builders;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Nodes;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.PostgreSQL.Repositories;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Sqlite;
using Testcontainers.PostgreSql;

namespace TerraformRegistry.Tests.UnitTests.Database;

public sealed class SqliteMirrorRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public SqliteMirrorRepositoryTests()
    {
        var dbName = $"MirrorRepoTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        new DbUpMigrator(NullLogger<DbUpMigrator>.Instance).Migrate("sqlite", _connectionString);
    }

    [Fact]
    public async Task ProviderRepositoryRoundTripsIndexesPackagesAndFailures()
    {
        var repository = new SqliteProviderMirrorRepository(_connectionString);

        await ProviderRepositoryContract.RoundTripsIndexesPackagesAndFailures(repository);
    }

    [Fact]
    public async Task ModuleRepositoryRoundTripsVersionsPackagesAndFailures()
    {
        var repository = new SqliteModuleMirrorRepository(_connectionString);

        await ModuleRepositoryContract.RoundTripsVersionsPackagesAndFailures(repository);
    }

    [Fact]
    public async Task LeaseRepositoryManagesAcquireHeartbeatReleaseAndExpiredTakeover()
    {
        var repository = new SqliteMirrorLeaseRepository(_connectionString);

        await MirrorLeaseRepositoryContract.ManagesAcquireHeartbeatReleaseAndExpiredTakeover(repository);
    }

    [Fact]
    public async Task PublicationRepositoryRoundTripsAllAttemptFields()
    {
        var repository = new SqliteModulePublicationRepository(_connectionString);

        await PublicationRepositoryContract.RoundTripsAllAttemptFields(repository);
    }

    [Fact]
    public async Task PublicationRepositoryCommitsCatalogUsingExpectedSnapshot()
    {
        var database = new SqliteDatabaseService(
            _connectionString,
            "http://localhost",
            NullLogger<SqliteDatabaseService>.Instance,
            new DbUpMigrator(NullLogger<DbUpMigrator>.Instance));

        await PublicationCommitContract.CommitsCatalogUsingExpectedSnapshot(database, database);
    }

    [Fact]
    public async Task ExtractionJobsLeaseClaimRetryAndDeadLetter()
    {
        var repository = new SqliteModulePublicationRepository(_connectionString);

        await ExtractionJobRepositoryContract.LeasesClaimsRetriesAndDeadLetters(repository, repository);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

[Trait("Category", "Integration")]
public sealed class PostgreSqlMirrorRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithDatabase("mirror_repository_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();
        new DbUpMigrator(NullLogger<DbUpMigrator>.Instance).Migrate("postgres", _connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProviderRepositoryRoundTripsIndexesPackagesAndFailures()
    {
        var repository = new PostgreSqlProviderMirrorRepository(_connectionString);

        await ProviderRepositoryContract.RoundTripsIndexesPackagesAndFailures(repository);
    }

    [Fact]
    public async Task ModuleRepositoryRoundTripsVersionsPackagesAndFailures()
    {
        var repository = new PostgreSqlModuleMirrorRepository(_connectionString);

        await ModuleRepositoryContract.RoundTripsVersionsPackagesAndFailures(repository);
    }

    [Fact]
    public async Task LeaseRepositoryManagesAcquireHeartbeatReleaseAndExpiredTakeover()
    {
        var repository = new PostgreSqlMirrorLeaseRepository(_connectionString);

        await MirrorLeaseRepositoryContract.ManagesAcquireHeartbeatReleaseAndExpiredTakeover(repository);
    }

    [Fact]
    public async Task PublicationRepositoryRoundTripsAllAttemptFields()
    {
        var repository = new PostgreSqlModulePublicationRepository(_connectionString);

        await PublicationRepositoryContract.RoundTripsAllAttemptFields(repository);
    }

    [Fact]
    public async Task PublicationRepositoryCommitsCatalogUsingExpectedSnapshot()
    {
        var database = new PostgreSqlDatabaseService(
            _connectionString,
            "http://localhost",
            NullLogger<PostgreSqlDatabaseService>.Instance,
            new DbUpMigrator(NullLogger<DbUpMigrator>.Instance));

        await PublicationCommitContract.CommitsCatalogUsingExpectedSnapshot(database, database);
    }

    [Fact]
    public async Task ExtractionJobsLeaseClaimRetryAndDeadLetter()
    {
        var repository = new PostgreSqlModulePublicationRepository(_connectionString);

        await ExtractionJobRepositoryContract.LeasesClaimsRetriesAndDeadLetters(repository, repository);
    }
}

internal static class PublicationRepositoryContract
{
    public static async Task RoundTripsAllAttemptFields(IModulePublicationRepository repository)
    {
        var now = TruncateToMicroseconds(DateTime.UtcNow);
        var attempt = new ModulePublicationAttempt
        {
            Id = Guid.NewGuid(),
            Namespace = "acme",
            Name = "network",
            Provider = "aws",
            Version = "1.0.0",
            State = ModulePublicationAttemptState.Failed,
            StagingKey = "publication-attempts/acme/network/aws/1.0.0/staged.zip",
            ExpectedRevision = "catalog-r17",
            CommittedRevision = "artifact-r17",
            Error = "promotion failed after compare-and-swap",
            CreatedAt = now,
            UpdatedAt = now.AddMinutes(1),
            CompletedAt = now.AddMinutes(2)
        };
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = attempt.Id,
            Namespace = attempt.Namespace,
            Name = attempt.Name,
            Provider = attempt.Provider,
            Version = attempt.Version,
            State = ModuleExtractionJobState.Pending,
            CreatedAt = attempt.CreatedAt,
            UpdatedAt = attempt.UpdatedAt
        };

        await repository.CreatePublicationAttemptWithExtractionJobAsync(attempt, job);

        Assert.Equal(attempt, await repository.GetPublicationAttemptAsync(attempt.Id));
        Assert.Equal(job, await repository.GetExtractionJobAsync(job.Id));
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}

internal static class PublicationCommitContract
{
    public static async Task CommitsCatalogUsingExpectedSnapshot(
        IModulePublicationRepository publications,
        IModuleRepository modules)
    {
        var now = TruncateToMicroseconds(DateTime.UtcNow);
        var first = CreateModule("artifacts/acme/network/aws/1.0.0/first.zip", now);
        var firstAttempt = CreateAttempt(first, now);
        var firstJob = CreateJob(firstAttempt);
        await publications.CreatePublicationAttemptWithExtractionJobAsync(firstAttempt, firstJob);

        Assert.Null(await ((IModuleExtractionJobRepository)publications)
            .TryClaimNextExtractionJobAsync("worker", TimeSpan.FromMinutes(1)));

        Assert.True(await publications.TryCommitStagedPublicationAsync(firstAttempt, first, null));
        AssertModuleEquals(first, await modules.GetModuleStorageAsync("acme", "network", "aws", "1.0.0"));
        Assert.Equal(ModulePublicationAttemptState.Committed,
            (await publications.GetPublicationAttemptAsync(firstAttempt.Id))!.State);
        Assert.Equal(ModuleExtractionJobState.Pending,
            (await publications.GetExtractionJobAsync(firstJob.Id))!.State);

        var replacement = CreateModule("artifacts/acme/network/aws/1.0.0/replacement.zip", now.AddMinutes(1));
        replacement.Description = "replacement artifact";
        var staleSnapshot = CreateModule("artifacts/acme/network/aws/1.0.0/stale.zip", now);
        var replacementAttempt = CreateAttempt(replacement, now.AddMinutes(1));
        await publications.CreatePublicationAttemptWithExtractionJobAsync(
            replacementAttempt,
            CreateJob(replacementAttempt));

        Assert.False(await publications.TryCommitStagedPublicationAsync(replacementAttempt, replacement, staleSnapshot));
        AssertModuleEquals(first, await modules.GetModuleStorageAsync("acme", "network", "aws", "1.0.0"));
        Assert.Equal(ModulePublicationAttemptState.Staged,
            (await publications.GetPublicationAttemptAsync(replacementAttempt.Id))!.State);

        Assert.False(await publications.TryFailStagedPublicationAsync(firstAttempt.Id, "loser cleanup"));
        Assert.True(await publications.TryFailStagedPublicationAsync(replacementAttempt.Id, "promotion failed"));
        var failedReplacement = await publications.GetPublicationAttemptAsync(replacementAttempt.Id);
        Assert.Equal(ModulePublicationAttemptState.Failed, failedReplacement!.State);
        Assert.Equal("promotion failed", failedReplacement.Error);
        Assert.NotNull(failedReplacement.CompletedAt);
        AssertModuleEquals(first, await modules.GetModuleStorageAsync("acme", "network", "aws", "1.0.0"));
    }

    private static ModulePublicationAttempt CreateAttempt(ModuleStorage module, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Namespace = module.Namespace,
        Name = module.Name,
        Provider = module.Provider,
        Version = module.Version,
        State = ModulePublicationAttemptState.Staged,
        StagingKey = $"staging/{Guid.NewGuid():N}",
        ExpectedRevision = "catalog-r17",
        CommittedRevision = "artifact-r17",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static ModuleExtractionJob CreateJob(ModulePublicationAttempt attempt) => new()
    {
        Id = Guid.NewGuid(),
        PublicationAttemptId = attempt.Id,
        Namespace = attempt.Namespace,
        Name = attempt.Name,
        Provider = attempt.Provider,
        Version = attempt.Version,
        State = ModuleExtractionJobState.Staged,
        CreatedAt = attempt.CreatedAt,
        UpdatedAt = attempt.UpdatedAt
    };

    private static ModuleStorage CreateModule(string path, DateTime publishedAt) => new()
    {
        Namespace = "acme",
        Name = "network",
        Provider = "aws",
        Version = "1.0.0",
        Description = "network artifact",
        FilePath = path,
        PublishedAt = publishedAt,
        Dependencies = ["hashicorp/aws"],
        Metadata = new ModuleArtifactMetadata
        {
            Source = new ModuleSourceInfo { Kind = "manual", SourceUrl = "https://example.test/network" }
        }
    };

    private static void AssertModuleEquals(ModuleStorage expected, ModuleStorage? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Description, actual!.Description);
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.PublishedAt, actual.PublishedAt);
        Assert.Equal(expected.Dependencies, actual.Dependencies);
        Assert.Equal(JsonSerializer.Serialize(expected.Metadata), JsonSerializer.Serialize(actual.Metadata));
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}

internal static class ProviderRepositoryContract
{
    public static async Task RoundTripsIndexesPackagesAndFailures(IProviderMirrorRepository repository)
    {
        var providerIndex = new MirrorProviderIndex
        {
            Hostname = "registry.terraform.io",
            Namespace = "hashicorp",
            Type = "aws",
            VersionsJson = """{"versions":["5.0.0"]}""",
            ETag = "\"provider-index\"",
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };

        await repository.UpsertProviderIndexAsync(providerIndex);

        var loadedIndex = await repository.GetProviderIndexAsync("registry.terraform.io", "hashicorp", "aws");
        Assert.NotNull(loadedIndex);
        MirrorJsonAssert.Equal(providerIndex.VersionsJson, loadedIndex!.VersionsJson);
        Assert.Equal(providerIndex.ETag, loadedIndex.ETag);
        Assert.Equal("ready", loadedIndex.State);

        var package = new MirrorProviderPackage
        {
            Hostname = "registry.terraform.io",
            Namespace = "hashicorp",
            Type = "aws",
            Version = "5.0.0",
            Os = "linux",
            Arch = "amd64",
            DownloadUrl = "https://releases.hashicorp.com/terraform-provider-aws.zip",
            Filename = "terraform-provider-aws_5.0.0_linux_amd64.zip",
            PackageStoragePath = "mirror/providers/hashicorp/aws/5.0.0/linux_amd64.zip",
            SizeBytes = 123456789,
            CacheSizeBytes = 123456999,
            ProtocolsJson = """["5.0"]""",
            HashesJson = """["h1:test","zh:test"]""",
            Shasum = new string('a', 64),
            SigningKeysJson = """{"gpg_public_keys":[]}""",
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };

        await repository.UpsertProviderPackageAsync(package);

        var loadedPackage = await repository.GetProviderPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64");

        Assert.NotNull(loadedPackage);
        MirrorJsonAssert.Equal(package.HashesJson, loadedPackage!.HashesJson);
        MirrorJsonAssert.Equal(package.ProtocolsJson, loadedPackage.ProtocolsJson);
        Assert.Equal(package.SizeBytes, loadedPackage.SizeBytes);
        Assert.Equal(package.CacheSizeBytes, loadedPackage.CacheSizeBytes);
        Assert.Equal(package.PackageStoragePath, loadedPackage.PackageStoragePath);

        var listed = await repository.ListProviderPackagesAsync("aws", "ready", 10, 0);
        var item = Assert.Single(listed);
        Assert.Equal(package.DownloadUrl, item.DownloadUrl);

        await repository.MarkProviderPackageFailedAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            "checksum mismatch",
            502);

        var failed = await repository.GetProviderPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64");

        Assert.NotNull(failed);
        Assert.Equal("failed", failed!.State);
        Assert.Equal("checksum mismatch", failed.LastError);
        Assert.Equal(502, failed.HttpStatusCode);

        await repository.MarkProviderPackageFailedAsync(
            "registry.terraform.io",
            "hashicorp",
            "random",
            "1.2.3",
            "darwin",
            "arm64",
            "first fetch failed",
            400);

        var coldFailed = await repository.GetProviderPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "random",
            "1.2.3",
            "darwin",
            "arm64");

        Assert.NotNull(coldFailed);
        Assert.Equal("failed", coldFailed!.State);
        Assert.Equal("first fetch failed", coldFailed.LastError);
        Assert.Equal(400, coldFailed.HttpStatusCode);
        Assert.Equal("https://registry.terraform.io/v1/providers/hashicorp/random/1.2.3/download/darwin/arm64", coldFailed.DownloadUrl);
    }
}

internal static class ModuleRepositoryContract
{
    public static async Task RoundTripsVersionsPackagesAndFailures(IModuleMirrorRepository repository)
    {
        var moduleVersions = new MirrorModuleVersions
        {
            Hostname = "registry.terraform.io",
            Namespace = "terraform-aws-modules",
            Name = "vpc",
            Provider = "aws",
            VersionsJson = """{"modules":[{"versions":[{"version":"1.0.0"}]}]}""",
            ETag = "\"module-versions\"",
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };

        await repository.UpsertModuleVersionsAsync(moduleVersions);

        var loadedVersions = await repository.GetModuleVersionsAsync(
            "registry.terraform.io",
            "terraform-aws-modules",
            "vpc",
            "aws");

        Assert.NotNull(loadedVersions);
        MirrorJsonAssert.Equal(moduleVersions.VersionsJson, loadedVersions!.VersionsJson);
        Assert.Equal(moduleVersions.ETag, loadedVersions.ETag);

        var package = new MirrorModulePackage
        {
            Hostname = "registry.terraform.io",
            Namespace = "terraform-aws-modules",
            Name = "vpc",
            Provider = "aws",
            Version = "1.0.0",
            DownloadUrl = "https://api.github.com/repos/terraform-aws-modules/terraform-aws-vpc/tarball/v1.0.0",
            Source = "github.com/terraform-aws-modules/terraform-aws-vpc",
            PackageStoragePath = "mirror/modules/terraform-aws-modules/vpc/aws/1.0.0.tgz",
            SizeBytes = 4567,
            CacheSizeBytes = 4567,
            MetadataJson = """{"root":{"readme":"README.md"}}""",
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };

        await repository.UpsertModulePackageAsync(package);

        var loadedPackage = await repository.GetModulePackageAsync(
            "registry.terraform.io",
            "terraform-aws-modules",
            "vpc",
            "aws",
            "1.0.0");

        Assert.NotNull(loadedPackage);
        MirrorJsonAssert.Equal(package.MetadataJson, loadedPackage!.MetadataJson);
        Assert.Equal(package.SizeBytes, loadedPackage.SizeBytes);
        Assert.Equal(package.CacheSizeBytes, loadedPackage.CacheSizeBytes);
        Assert.Equal(package.PackageStoragePath, loadedPackage.PackageStoragePath);

        var listed = await repository.ListModulePackagesAsync("vpc", "ready", 10, 0);
        var item = Assert.Single(listed);
        Assert.Equal(package.DownloadUrl, item.DownloadUrl);

        await repository.MarkModulePackageFailedAsync(
            "registry.terraform.io",
            "terraform-aws-modules",
            "vpc",
            "aws",
            "1.0.0",
            "download timed out",
            504);

        var failed = await repository.GetModulePackageAsync(
            "registry.terraform.io",
            "terraform-aws-modules",
            "vpc",
            "aws",
            "1.0.0");

        Assert.NotNull(failed);
        Assert.Equal("failed", failed!.State);
        Assert.Equal("download timed out", failed.LastError);
        Assert.Equal(504, failed.HttpStatusCode);
    }

}

internal static class MirrorJsonAssert
{
    public static void Equal(string? expected, string? actual)
    {
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected!), JsonNode.Parse(actual!)),
            $"Expected JSON {expected}, got {actual}");
    }
}

internal static class MirrorLeaseRepositoryContract
{
    public static async Task ManagesAcquireHeartbeatReleaseAndExpiredTakeover(IMirrorLeaseRepository repository)
    {
        var cancellationToken = CancellationToken.None;

        var acquired = await repository.TryAcquireAsync(
            "provider:registry.terraform.io/hashicorp/aws",
            "provider-package-sync",
            "worker-a",
            TimeSpan.FromMinutes(5),
            cancellationToken);

        Assert.NotNull(acquired);
        Assert.Equal("provider:registry.terraform.io/hashicorp/aws", acquired!.LeaseKey);
        Assert.Equal("provider-package-sync", acquired.OperationType);
        Assert.Equal("worker-a", acquired.OwnerInstanceId);

        var blocked = await repository.TryAcquireAsync(
            acquired.LeaseKey,
            acquired.OperationType,
            "worker-b",
            TimeSpan.FromMinutes(5),
            cancellationToken);

        Assert.Null(blocked);

        var sameOwnerBlocked = await repository.TryAcquireAsync(
            acquired.LeaseKey,
            acquired.OperationType,
            "worker-a",
            TimeSpan.FromMinutes(5),
            cancellationToken);

        Assert.Null(sameOwnerBlocked);

        var heartbeatSucceeded = await repository.HeartbeatAsync(
            acquired.Id,
            acquired.LeaseKey,
            "worker-a",
            TimeSpan.FromMinutes(10),
            cancellationToken);

        Assert.True(heartbeatSucceeded);

        var heartbeated = await repository.GetLeaseAsync(acquired.LeaseKey, cancellationToken);
        Assert.NotNull(heartbeated);
        Assert.Equal("worker-a", heartbeated!.OwnerInstanceId);
        Assert.NotNull(heartbeated.HeartbeatAt);
        Assert.True(heartbeated.ExpiresAt > acquired.ExpiresAt);

        Assert.False(await repository.ReleaseAsync(acquired.Id, acquired.LeaseKey, "worker-b", cancellationToken));
        Assert.True(await repository.ReleaseAsync(acquired.Id, acquired.LeaseKey, "worker-a", cancellationToken));
        Assert.Null(await repository.GetLeaseAsync(acquired.LeaseKey, cancellationToken));

        await repository.UpsertLeaseAsync(new MirrorCacheLease
        {
            LeaseKey = "module:registry.terraform.io/terraform-aws-modules/vpc/aws",
            OperationType = "module-package-sync",
            OwnerInstanceId = "stale-worker",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            HeartbeatAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        }, cancellationToken);

        var takeover = await repository.TryAcquireAsync(
            "module:registry.terraform.io/terraform-aws-modules/vpc/aws",
            "module-package-sync",
            "worker-c",
            TimeSpan.FromMinutes(5),
            cancellationToken);

        Assert.NotNull(takeover);
        Assert.Equal("worker-c", takeover!.OwnerInstanceId);
        Assert.False(await repository.HeartbeatAsync(takeover.Id, takeover.LeaseKey, "stale-worker", TimeSpan.FromMinutes(5), cancellationToken));

        var staleHandleKey = "provider:registry.terraform.io/hashicorp/stale-handle";
        var staleHandle = await repository.TryAcquireAsync(
            staleHandleKey,
            "provider-package-sync",
            "worker-d",
            TimeSpan.FromMinutes(5),
            cancellationToken);
        Assert.NotNull(staleHandle);

        await repository.UpsertLeaseAsync(staleHandle! with
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            HeartbeatAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        }, cancellationToken);

        var reacquired = await repository.TryAcquireAsync(
            staleHandleKey,
            "provider-package-sync",
            "worker-d",
            TimeSpan.FromMinutes(5),
            cancellationToken);
        Assert.NotNull(reacquired);
        Assert.NotEqual(staleHandle.Id, reacquired!.Id);
        Assert.False(await repository.HeartbeatAsync(
            staleHandle.Id,
            staleHandle.LeaseKey,
            staleHandle.OwnerInstanceId,
            TimeSpan.FromMinutes(5),
            cancellationToken));
        Assert.False(await repository.ReleaseAsync(
            staleHandle.Id,
            staleHandle.LeaseKey,
            staleHandle.OwnerInstanceId,
            cancellationToken));
        Assert.True(await repository.HeartbeatAsync(
            reacquired.Id,
            reacquired.LeaseKey,
            reacquired.OwnerInstanceId,
            TimeSpan.FromMinutes(5),
            cancellationToken));

        var expiredHeartbeatKey = "provider:registry.terraform.io/hashicorp/expired-heartbeat";
        var originalExpiry = DateTime.UtcNow.AddMinutes(-1);
        await repository.UpsertLeaseAsync(new MirrorCacheLease
        {
            LeaseKey = expiredHeartbeatKey,
            OperationType = "provider-package-sync",
            OwnerInstanceId = "expired-worker",
            ExpiresAt = originalExpiry,
            HeartbeatAt = DateTime.UtcNow.AddMinutes(-2),
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        }, cancellationToken);

        var expiredBeforeHeartbeat = await repository.GetLeaseAsync(expiredHeartbeatKey, cancellationToken);
        Assert.NotNull(expiredBeforeHeartbeat);

        Assert.False(await repository.HeartbeatAsync(
            expiredBeforeHeartbeat!.Id,
            expiredHeartbeatKey,
            "expired-worker",
            TimeSpan.FromMinutes(5),
            cancellationToken));

        var expiredAfterHeartbeat = await repository.GetLeaseAsync(expiredHeartbeatKey, cancellationToken);
        Assert.NotNull(expiredAfterHeartbeat);
        Assert.Equal("expired-worker", expiredAfterHeartbeat!.OwnerInstanceId);
        Assert.True(expiredAfterHeartbeat.ExpiresAt <= DateTime.UtcNow);

        var racedKey = $"module:registry.terraform.io/raced/{Guid.NewGuid():N}";
        var contenders = Enumerable.Range(0, 8)
            .Select(i => repository.TryAcquireAsync(
                racedKey,
                "module-package-sync",
                $"racing-worker-{i}",
                TimeSpan.FromMinutes(5),
                cancellationToken))
            .ToArray();

        var raceResults = await Task.WhenAll(contenders);
        var winners = raceResults.Where(result => result is not null).ToArray();

        var persistedWinner = await repository.GetLeaseAsync(racedKey, cancellationToken);
        Assert.Single(winners);
        Assert.NotNull(persistedWinner);
        Assert.Equal(winners[0]!.OwnerInstanceId, persistedWinner!.OwnerInstanceId);
    }
}
