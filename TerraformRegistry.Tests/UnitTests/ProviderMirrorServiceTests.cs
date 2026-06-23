using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class ProviderMirrorServiceTests
{
    [Fact]
    public async Task ProviderIndexFetchesUpstreamVersionsAndDoesNotDownloadPackages()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/versions", UpstreamVersionsJson());
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(http, repo: repo);

        var index = await service.GetProviderIndexAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            CancellationToken.None);

        Assert.NotNull(index);
        Assert.True(index!.Versions.ContainsKey("5.0.0"));
        Assert.DoesNotContain(http.Requests, uri => uri.AbsolutePath.Contains("/download/", StringComparison.Ordinal));
        repo.Verify(x => x.UpsertProviderIndexAsync(It.Is<MirrorProviderIndex>(cached =>
            cached.State == "ready" &&
            cached.VersionsJson.Contains("\"5.0.0\"", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task ProviderVersionDownloadsCachesAndReturnsSignedArchiveWithTerraformHash()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var filename = "terraform-provider-aws_5.0.0_linux_amd64.zip";
        var http = new RecordingHttpMessageHandler();
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/versions", UpstreamVersionsJson());
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/5.0.0/download/linux/amd64", JsonSerializer.Serialize(new
        {
            protocols = new[] { "5.0" },
            os = "linux",
            arch = "amd64",
            filename,
            download_url = "https://releases.example.com/pkg.zip",
            shasums_url = "https://releases.example.com/SHA256SUMS",
            shasums_signature_url = "https://releases.example.com/SHA256SUMS.sig",
            shasum,
            signing_keys = new { gpg_public_keys = Array.Empty<object>() }
        }));
        http.RespondBytes("https://releases.example.com/pkg.zip", packageBytes, "application/zip");
        http.RespondText("https://releases.example.com/SHA256SUMS", $"{shasum}  {filename}\n");
        http.RespondBytes("https://releases.example.com/SHA256SUMS.sig", [9, 8, 7], "application/octet-stream");
        var storage = new InMemoryProviderArtifactStorage();
        var repo = new Mock<IProviderMirrorRepository>();
        repo.Setup(x => x.GetProviderPackageAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "5.0.0",
                "linux",
                "amd64"))
            .ReturnsAsync((MirrorProviderPackage?)null);
        var service = CreateService(http, repo, storage);

        var version = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.NotNull(version);
        var archive = Assert.Single(version!.Archives);
        Assert.Equal("linux_amd64", archive.Key);
        Assert.StartsWith("/mirror/providers/registry.terraform.io/hashicorp/aws/terraform-provider-aws_5.0.0_linux_amd64.zip?", archive.Value.Url);
        var hash = Assert.Single(archive.Value.Hashes);
        Assert.Equal($"zh:{shasum}", hash);
        Assert.DoesNotContain(shasum, archive.Value.Hashes);
        Assert.Contains(storage.Paths, path => path.EndsWith(filename, StringComparison.Ordinal));
        Assert.Contains(storage.Paths, path => path.EndsWith("terraform-provider-aws_5.0.0_SHA256SUMS", StringComparison.Ordinal));
        Assert.Contains(storage.Paths, path => path.EndsWith("terraform-provider-aws_5.0.0_SHA256SUMS.sig", StringComparison.Ordinal));
        repo.Verify(x => x.UpsertProviderPackageAsync(It.Is<MirrorProviderPackage>(package =>
            package.State == "ready" &&
            package.PackageStoragePath != null &&
            package.HashesJson == JsonSerializer.Serialize(new[] { $"zh:{shasum}" }) &&
            package.SigningKeysJson != null &&
            package.SigningKeysJson.Contains("\"signature_verification\":\"not_verified\"", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task ProviderVersionMarksSignatureVerificationNotVerifiedWhenSignatureIsPreserved()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            signatureBytes: [0, 1, 2, 3]);
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(http, repo: repo);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.NotNull(result);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.Is<MirrorProviderPackage>(package =>
            package.SigningKeysJson != null &&
            package.SigningKeysJson.Contains("\"signature_verification\":\"not_verified\"", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task ProviderVersionRejectsUpstreamMetadataForWrongPlatform()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            metadataOs: "darwin",
            metadataArch: "arm64",
            filename: "terraform-provider-aws_5.0.0_darwin_arm64.zip");
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(http, repo: repo);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.IsAny<MirrorProviderPackage>()), Times.Never);
        repo.Verify(x => x.MarkProviderPackageFailedAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            It.Is<string>(message => message.Contains("platform", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }

    [Fact]
    public async Task ProviderVersionRejectsPathLikeUpstreamFilenameBeforeStorage()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            filename: "../terraform-provider-aws_5.0.0_linux_amd64.zip");
        var storage = new InMemoryProviderArtifactStorage();
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(http, repo: repo, storage: storage);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(storage.Paths);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.IsAny<MirrorProviderPackage>()), Times.Never);
    }

    [Fact]
    public async Task ProviderVersionFirstFetchFailurePersistsFailedPackageState()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            filename: "bad.zip");
        var repo = new InMemoryProviderMirrorRepository();
        var service = CreateServiceWithRepository(http, repo);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        var failed = await repo.GetProviderPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64");
        Assert.NotNull(failed);
        Assert.Equal("failed", failed!.State);
        Assert.Contains("filename", failed.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderVersionReleasesLeaseWithUncancelledTokenWhenRequestIsCancelled()
    {
        var http = new RecordingHttpMessageHandler();
        var repo = new Mock<IProviderMirrorRepository>();
        repo.Setup(x => x.GetProviderIndexAsync("registry.terraform.io", "hashicorp", "aws"))
            .ReturnsAsync(new MirrorProviderIndex
            {
                Hostname = "registry.terraform.io",
                Namespace = "hashicorp",
                Type = "aws",
                VersionsJson = UpstreamVersionsJson(),
                State = "ready",
                LastSyncAt = DateTime.UtcNow
            });
        repo.Setup(x => x.GetProviderPackageAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "5.0.0",
                "linux",
                "amd64"))
            .ReturnsAsync((MirrorProviderPackage?)null);
        ArrangeProviderVersionResponses(
            http,
            [1, 2, 3],
            "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81");
        var policy = new Mock<IMirrorPolicyService>();
        policy.Setup(x => x.IsProviderAllowedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        policy.Setup(x => x.ValidateProviderArtifactUrlAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new ValidatedMirrorEndpoint(new Uri(url), [IPAddress.Parse("93.184.216.34")]));
            });
        var lease = new Mock<IMirrorLeaseService>();
        var handle = new MirrorLeaseHandle
        {
            Id = Guid.NewGuid(),
            LeaseKey = "provider-package",
            OperationType = "provider-package",
            OwnerInstanceId = "test",
            ExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        lease.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                "provider-package",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(handle);
        CancellationToken releaseToken = default;
        lease.Setup(x => x.ReleaseAsync(handle, It.IsAny<CancellationToken>()))
            .Callback<MirrorLeaseHandle, CancellationToken>((_, token) => releaseToken = token)
            .ReturnsAsync(true);
        var service = CreateService(
            http,
            repo: repo,
            policy: policy,
            lease: lease);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            cts.Token));

        lease.Verify(x => x.ReleaseAsync(handle, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(releaseToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ProviderVersionRejectsHttpArtifactUrlBeforeFetch()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            downloadUrl: "http://releases.example.com/pkg.zip");
        http.RespondBytes("http://releases.example.com/pkg.zip", packageBytes, "application/zip");
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(http, repo: repo, policyService: CreateRealPolicy(new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(http.Requests, uri => uri.Scheme == Uri.UriSchemeHttp);
        repo.Verify(x => x.MarkProviderPackageFailedAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            It.Is<string>(message => message.Contains("HTTPS", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }

    [Fact]
    public async Task ProviderVersionRejectsPrivateArtifactUrlBeforeFetch()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            downloadUrl: "https://private.example.com/pkg.zip");
        http.RespondBytes("https://private.example.com/pkg.zip", packageBytes, "application/zip");
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(
            http,
            repo: repo,
            policyService: CreateRealPolicy(new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)
            {
                ["private.example.com"] = [IPAddress.Loopback]
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(http.Requests, uri => uri.Host == "private.example.com");
    }

    [Fact]
    public async Task ProviderVersionFollowsValidatedPublicArtifactRedirect()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            downloadUrl: "https://releases.example.com/pkg.zip",
            registerPackage: false);
        http.RespondRedirect("https://releases.example.com/pkg.zip", "https://cdn.example.com/pkg.zip");
        http.RespondBytes("https://cdn.example.com/pkg.zip", packageBytes, "application/zip");
        var service = CreateService(
            http,
            policyService: CreateRealPolicy(new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)
            {
                ["releases.example.com"] = [IPAddress.Parse("93.184.216.34")],
                ["cdn.example.com"] = [IPAddress.Parse("93.184.216.35")]
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(http.Requests, uri => uri.Host == "releases.example.com");
        Assert.Contains(http.Requests, uri => uri.Host == "cdn.example.com");
    }

    [Fact]
    public async Task ProviderVersionRejectsRedirectToPrivateArtifactUrlBeforeFetchingRedirectTarget()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            downloadUrl: "https://releases.example.com/pkg.zip",
            registerPackage: false);
        http.RespondRedirect("https://releases.example.com/pkg.zip", "https://private.example.com/pkg.zip");
        http.RespondBytes("https://private.example.com/pkg.zip", packageBytes, "application/zip");
        var service = CreateService(
            http,
            policyService: CreateRealPolicy(new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)
            {
                ["releases.example.com"] = [IPAddress.Parse("93.184.216.34")],
                ["private.example.com"] = [IPAddress.Loopback]
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        Assert.Contains(http.Requests, uri => uri.Host == "releases.example.com");
        Assert.DoesNotContain(http.Requests, uri => uri.Host == "private.example.com");
    }

    [Fact]
    public async Task ProviderVersionEnforcesPackageMaxBytes()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(http, packageBytes, shasum);
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(
            http,
            repo: repo,
            configuration: CreateConfiguration(new Dictionary<string, string?>
            {
                ["Mirror:Providers:MaxPackageBytes"] = "2"
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.IsAny<MirrorProviderPackage>()), Times.Never);
    }

    [Fact]
    public async Task ProviderVersionEnforcesChecksumMaxBytesForShasums()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            shasumsText: $"{shasum}  {ExpectedFilename}\n");
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(
            http,
            repo: repo,
            configuration: CreateConfiguration(new Dictionary<string, string?>
            {
                ["Mirror:Providers:MaxChecksumBytes"] = "2"
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.IsAny<MirrorProviderPackage>()), Times.Never);
    }

    [Fact]
    public async Task ProviderVersionEnforcesChecksumMaxBytesForSignature()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var http = new RecordingHttpMessageHandler();
        ArrangeProviderVersionResponses(
            http,
            packageBytes,
            shasum,
            signatureBytes: [9, 8, 7]);
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(
            http,
            repo: repo,
            configuration: CreateConfiguration(new Dictionary<string, string?>
            {
                ["Mirror:Providers:MaxChecksumBytes"] = "2"
            }));

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        repo.Verify(x => x.UpsertProviderPackageAsync(It.IsAny<MirrorProviderPackage>()), Times.Never);
    }

    [Fact]
    public async Task ProviderVersionDeniedCoordinateDoesNotFetchOrCache()
    {
        var http = new RecordingHttpMessageHandler();
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/versions", UpstreamVersionsJson());
        var repo = new Mock<IProviderMirrorRepository>();
        var policy = new Mock<IMirrorPolicyService>();
        policy.Setup(x => x.IsProviderAllowedAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                string.Empty,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(http, repo: repo, policy: policy);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(http.Requests);
        repo.Verify(x => x.UpsertProviderIndexAsync(It.IsAny<MirrorProviderIndex>()), Times.Never);
    }

    [Fact]
    public async Task ProviderVersionFiltersDeniedPlatformWithoutFetchingMetadata()
    {
        var packageBytes = new byte[] { 1, 2, 3 };
        var shasum = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var filename = "terraform-provider-aws_5.0.0_linux_amd64.zip";
        var http = new RecordingHttpMessageHandler();
        http.RespondJson(
            "https://registry.example.com/v1/providers/hashicorp/aws/versions",
            UpstreamVersionsJson("linux_amd64", "darwin_arm64"));
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/5.0.0/download/linux/amd64", JsonSerializer.Serialize(new
        {
            protocols = new[] { "5.0" },
            os = "linux",
            arch = "amd64",
            filename,
            download_url = "https://releases.example.com/pkg.zip",
            shasums_url = "https://releases.example.com/SHA256SUMS",
            shasums_signature_url = "https://releases.example.com/SHA256SUMS.sig",
            shasum,
            signing_keys = new { gpg_public_keys = Array.Empty<object>() }
        }));
        http.RespondBytes("https://releases.example.com/pkg.zip", packageBytes, "application/zip");
        http.RespondText("https://releases.example.com/SHA256SUMS", $"{shasum}  {filename}\n");
        http.RespondBytes("https://releases.example.com/SHA256SUMS.sig", [9, 8, 7], "application/octet-stream");
        var repo = new Mock<IProviderMirrorRepository>();
        var policy = new Mock<IMirrorPolicyService>();
        policy.Setup(x => x.IsProviderAllowedAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                string.Empty,
                string.Empty,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        policy.Setup(x => x.IsProviderAllowedAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "linux",
                "amd64",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        policy.Setup(x => x.IsProviderAllowedAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "darwin",
                "arm64",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(http, repo: repo, policy: policy);

        var result = await service.GetProviderVersionAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            CancellationToken.None);

        Assert.NotNull(result);
        var archive = Assert.Single(result!.Archives);
        Assert.Equal("linux_amd64", archive.Key);
        Assert.DoesNotContain(http.Requests, uri => uri.AbsolutePath.Contains("/download/darwin/arm64", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenPackageWithValidSignedUrlOpensCachedStorage()
    {
        var configuration = CreateConfiguration();
        var storage = new InMemoryProviderArtifactStorage();
        storage.Seed("cached.zip", [1, 2, 3]);
        var repo = new Mock<IProviderMirrorRepository>();
        repo.Setup(x => x.GetProviderPackageAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "5.0.0",
                "linux",
                "amd64"))
            .ReturnsAsync(ReadyPackage(filename: ExpectedFilename, storagePath: "cached.zip"));
        var service = CreateService(new RecordingHttpMessageHandler(), repo: repo, storage: storage, configuration: configuration);
        var url = CreateSignedPackageUrl(configuration, filename: ExpectedFilename, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var download = await service.OpenPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            ExpectedFilename,
            QueryFromUrl(url),
            CancellationToken.None);

        Assert.NotNull(download);
        using var buffer = new MemoryStream();
        await download!.Content.CopyToAsync(buffer);
        Assert.Equal([1, 2, 3], buffer.ToArray());
    }

    [Fact]
    public async Task OpenPackageRejectsExpiredAndTamperedSignedUrls()
    {
        var configuration = CreateConfiguration();
        var storage = new InMemoryProviderArtifactStorage();
        storage.Seed("cached.zip", [1, 2, 3]);
        var repo = new Mock<IProviderMirrorRepository>();
        var service = CreateService(new RecordingHttpMessageHandler(), repo: repo, storage: storage, configuration: configuration);
        var expired = CreateSignedPackageUrl(configuration, filename: ExpectedFilename, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var tampered = CreateSignedPackageUrl(configuration, filename: ExpectedFilename, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10))
            .Replace("arch=amd64", "arch=arm64", StringComparison.Ordinal);

        var expiredDownload = await service.OpenPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            ExpectedFilename,
            QueryFromUrl(expired),
            CancellationToken.None);
        var tamperedDownload = await service.OpenPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            ExpectedFilename,
            QueryFromUrl(tampered),
            CancellationToken.None);

        Assert.Null(expiredDownload);
        Assert.Null(tamperedDownload);
        repo.Verify(x => x.GetProviderPackageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OpenPackageMissingArtifactMarksPackageFailedAndReturnsNull()
    {
        var configuration = CreateConfiguration();
        var repo = new Mock<IProviderMirrorRepository>();
        repo.Setup(x => x.GetProviderPackageAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "5.0.0",
                "linux",
                "amd64"))
            .ReturnsAsync(ReadyPackage(filename: ExpectedFilename, storagePath: "missing.zip"));
        var service = CreateService(
            new RecordingHttpMessageHandler(),
            repo: repo,
            storage: new InMemoryProviderArtifactStorage(),
            configuration: configuration);
        var url = CreateSignedPackageUrl(configuration, filename: ExpectedFilename, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var download = await service.OpenPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            ExpectedFilename,
            QueryFromUrl(url),
            CancellationToken.None);

        Assert.Null(download);
        repo.Verify(x => x.MarkProviderPackageFailedAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            It.Is<string>(message => message.Contains("missing", StringComparison.OrdinalIgnoreCase)),
            null), Times.Once);
    }

    [Fact]
    public async Task OpenPackageRejectsCachedPackageWithMismatchedFilename()
    {
        var configuration = CreateConfiguration();
        var storage = new InMemoryProviderArtifactStorage();
        storage.Seed("cached.zip", [1, 2, 3]);
        var repo = new Mock<IProviderMirrorRepository>();
        repo.Setup(x => x.GetProviderPackageAsync(
                "registry.terraform.io",
                "hashicorp",
                "aws",
                "5.0.0",
                "linux",
                "amd64"))
            .ReturnsAsync(ReadyPackage(filename: "terraform-provider-aws_5.0.0_linux_arm64.zip", storagePath: "cached.zip"));
        var service = CreateService(new RecordingHttpMessageHandler(), repo: repo, storage: storage, configuration: configuration);
        var url = CreateSignedPackageUrl(configuration, filename: ExpectedFilename, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var download = await service.OpenPackageAsync(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            ExpectedFilename,
            QueryFromUrl(url),
            CancellationToken.None);

        Assert.Null(download);
        Assert.Equal(0, storage.OpenReadCount);
    }

    private const string ExpectedFilename = "terraform-provider-aws_5.0.0_linux_amd64.zip";

    private static ProviderMirrorService CreateService(
        RecordingHttpMessageHandler http,
        Mock<IProviderMirrorRepository>? repo = null,
        IProviderArtifactStorage? storage = null,
        Mock<IMirrorPolicyService>? policy = null,
        IMirrorPolicyService? policyService = null,
        Mock<IMirrorLeaseService>? lease = null,
        IConfiguration? configuration = null)
    {
        configuration ??= CreateConfiguration();
        var settings = new Mock<IRuntimeSettingsService>();
        var configService = new MirrorConfigService(configuration, settings.Object);
        if (policyService is null && policy is null)
        {
            policy = new Mock<IMirrorPolicyService>();
            policy.Setup(x => x.IsProviderAllowedAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            policy.Setup(x => x.ValidateProviderArtifactUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) =>
                    new ValidatedMirrorEndpoint(new Uri(url), [IPAddress.Parse("93.184.216.34")]));
        }
        else if (policyService is null && policy is not null)
        {
            policy.Setup(x => x.ValidateProviderArtifactUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) =>
                    new ValidatedMirrorEndpoint(new Uri(url), [IPAddress.Parse("93.184.216.34")]));
        }
        if (lease is null)
        {
            lease = new Mock<IMirrorLeaseService>();
            lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MirrorLeaseHandle
                {
                    Id = Guid.NewGuid(),
                    LeaseKey = "lease",
                    OperationType = "provider-package",
                    OwnerInstanceId = "test",
                    ExpiresAt = DateTime.UtcNow.AddMinutes(1)
                });
        }

        return new ProviderMirrorService(
            repo?.Object ?? Mock.Of<IProviderMirrorRepository>(),
            storage ?? new InMemoryProviderArtifactStorage(),
            policyService ?? policy!.Object,
            configService,
            lease.Object,
            new SingleClientFactory(new HttpClient(http)),
            new MirrorPackageUrlSigner(configuration, new TestHostEnvironment()),
            NullLogger<ProviderMirrorService>.Instance);
    }

    private static ProviderMirrorService CreateServiceWithRepository(
        RecordingHttpMessageHandler http,
        IProviderMirrorRepository repository)
    {
        var configuration = CreateConfiguration();
        var settings = new Mock<IRuntimeSettingsService>();
        var configService = new MirrorConfigService(configuration, settings.Object);
        var policy = new Mock<IMirrorPolicyService>();
        policy.Setup(x => x.IsProviderAllowedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        policy.Setup(x => x.ValidateProviderArtifactUrlAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) =>
                new ValidatedMirrorEndpoint(new Uri(url), [IPAddress.Parse("93.184.216.34")]));
        var lease = new Mock<IMirrorLeaseService>();
        lease.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorLeaseHandle
            {
                Id = Guid.NewGuid(),
                LeaseKey = "lease",
                OperationType = "provider-package",
                OwnerInstanceId = "test",
                ExpiresAt = DateTime.UtcNow.AddMinutes(1)
            });

        return new ProviderMirrorService(
            repository,
            new InMemoryProviderArtifactStorage(),
            policy.Object,
            configService,
            lease.Object,
            new SingleClientFactory(new HttpClient(http)),
            new MirrorPackageUrlSigner(configuration, new TestHostEnvironment()),
            NullLogger<ProviderMirrorService>.Instance);
    }

    private static IConfiguration CreateConfiguration(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mirror:Enabled"] = "true",
            ["Mirror:UpstreamRegistryBaseUrl"] = "https://registry.example.com",
            ["Mirror:Providers:Enabled"] = "true",
            ["Mirror:Providers:AllowedHostnames:0"] = "registry.terraform.io",
            ["Mirror:PackageUrlSigningKey"] = "provider-mirror-service-signing-key",
            ["Oidc:JwtSecretKey"] = "provider-mirror-service-jwt-secret-key"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IMirrorPolicyService CreateRealPolicy(IReadOnlyDictionary<string, IPAddress[]> addressesByHost)
    {
        var configService = new Mock<IMirrorConfigService>();
        configService.Setup(x => x.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorConfigResponse { Effective = new MirrorOptions { Enabled = true } });

        return new MirrorPolicyService(
            configService.Object,
            new StubWebhookHostResolver(addressesByHost),
            NullLogger<MirrorPolicyService>.Instance);
    }

    private static void ArrangeProviderVersionResponses(
        RecordingHttpMessageHandler http,
        byte[] packageBytes,
        string shasum,
        string? downloadUrl = null,
        string? shasumsUrl = null,
        string? signatureUrl = null,
        string? shasumsText = null,
        byte[]? signatureBytes = null,
        string metadataOs = "linux",
        string metadataArch = "amd64",
        string? filename = null,
        bool registerPackage = true)
    {
        filename ??= ExpectedFilename;
        downloadUrl ??= "https://releases.example.com/pkg.zip";
        shasumsUrl ??= "https://releases.example.com/SHA256SUMS";
        signatureUrl ??= "https://releases.example.com/SHA256SUMS.sig";
        shasumsText ??= $"{shasum}  {filename}\n";
        signatureBytes ??= [9, 8, 7];

        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/versions", UpstreamVersionsJson());
        http.RespondJson("https://registry.example.com/v1/providers/hashicorp/aws/5.0.0/download/linux/amd64", JsonSerializer.Serialize(new
        {
            protocols = new[] { "5.0" },
            os = metadataOs,
            arch = metadataArch,
            filename,
            download_url = downloadUrl,
            shasums_url = shasumsUrl,
            shasums_signature_url = signatureUrl,
            shasum,
            signing_keys = new { gpg_public_keys = Array.Empty<object>() }
        }));

        if (registerPackage)
        {
            http.RespondBytes(downloadUrl, packageBytes, "application/zip");
        }

        http.RespondText(shasumsUrl, shasumsText);
        http.RespondBytes(signatureUrl, signatureBytes, "application/octet-stream");
    }

    private static string UpstreamVersionsJson(params string[] platforms)
    {
        if (platforms.Length == 0)
        {
            platforms = ["linux_amd64"];
        }

        return JsonSerializer.Serialize(new
        {
            versions = new[]
            {
                new
                {
                    version = "5.0.0",
                    protocols = new[] { "5.0" },
                    platforms = platforms.Select(platform =>
                    {
                        var parts = platform.Split('_', 2);
                        return new { os = parts[0], arch = parts[1] };
                    })
                }
            }
        });
    }

    private static MirrorProviderPackage ReadyPackage(string filename, string storagePath)
    {
        return new MirrorProviderPackage
        {
            Hostname = "registry.terraform.io",
            Namespace = "hashicorp",
            Type = "aws",
            Version = "5.0.0",
            Os = "linux",
            Arch = "amd64",
            DownloadUrl = "https://releases.example.com/pkg.zip",
            Filename = filename,
            PackageStoragePath = storagePath,
            SizeBytes = 3,
            ProtocolsJson = """["5.0"]""",
            HashesJson = """["zh:abc"]""",
            Shasum = "abc",
            State = "ready",
            LastSyncAt = DateTime.UtcNow
        };
    }

    private static string CreateSignedPackageUrl(
        IConfiguration configuration,
        string filename,
        DateTimeOffset expiresAt)
    {
        var signer = new MirrorPackageUrlSigner(configuration, new TestHostEnvironment());
        return signer.CreateSignedPackageUrl(
            "registry.terraform.io",
            "hashicorp",
            "aws",
            "5.0.0",
            "linux",
            "amd64",
            filename,
            expiresAt);
    }

    private static IReadOnlyDictionary<string, string[]> QueryFromUrl(string url)
    {
        var uri = new Uri(new Uri("http://localhost"), url);
        return QueryHelpers.ParseQuery(uri.Query).ToDictionary(
            x => x.Key,
            x => x.Value.Where(value => value is not null).Select(value => value!).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses = new(StringComparer.Ordinal);
        public List<Uri> Requests { get; } = [];

        public void RespondJson(string url, string json)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public void RespondText(string url, string text)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(text, Encoding.UTF8, "text/plain")
            };
        }

        public void RespondBytes(string url, byte[] bytes, string contentType)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            _responses[url].Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        public void RespondRedirect(string url, string location)
        {
            _responses[url] = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri(location) }
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (!_responses.TryGetValue(request.RequestUri!.ToString(), out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(Clone(response));
        }

        private static HttpResponseMessage Clone(HttpResponseMessage response)
        {
            var clone = new HttpResponseMessage(response.StatusCode);
            clone.Headers.Location = response.Headers.Location;
            if (response.Content is null)
            {
                return clone;
            }

            if (response.Content is StringContent stringContent)
            {
                var body = stringContent.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType);
            }
            else
            {
                var body = response.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(body);
                clone.Content.Headers.ContentType = response.Content.Headers.ContentType;
            }

            return clone;
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubWebhookHostResolver(IReadOnlyDictionary<string, IPAddress[]> addressesByHost) : IWebhookHostResolver
    {
        public Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
        {
            return Task.FromResult(addressesByHost.TryGetValue(host, out var addresses) ? addresses : [IPAddress.Parse("93.184.216.34")]);
        }
    }

    private sealed class InMemoryProviderArtifactStorage : IProviderArtifactStorage
    {
        private readonly Dictionary<string, byte[]> _content = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Paths => _content.Keys;
        public int OpenReadCount { get; private set; }

        public void Seed(string storagePath, byte[] content)
        {
            _content[storagePath] = content;
        }

        public async Task<ProviderArtifactSaveResult> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _content[relativePath] = buffer.ToArray();
            return new ProviderArtifactSaveResult(relativePath, buffer.Length);
        }

        public Task<string> CreateDownloadUrlAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(OpenRead(storagePath));

        private Stream? OpenRead(string storagePath)
        {
            OpenReadCount++;
            return _content.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null;
        }

        public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult(_content.ContainsKey(storagePath));

        public Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult(_content.Remove(storagePath));

        public Task<(bool Healthy, string? Reason)> CheckStorageAsync(CancellationToken cancellationToken) =>
            Task.FromResult((true, (string?)null));
    }

    private sealed class InMemoryProviderMirrorRepository : IProviderMirrorRepository
    {
        private readonly Dictionary<string, MirrorProviderIndex> _indexes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MirrorProviderPackage> _packages = new(StringComparer.Ordinal);

        public Task<MirrorProviderIndex?> GetProviderIndexAsync(string hostname, string providerNamespace, string type)
        {
            _indexes.TryGetValue($"{hostname}/{providerNamespace}/{type}", out var index);
            return Task.FromResult(index);
        }

        public Task UpsertProviderIndexAsync(MirrorProviderIndex providerIndex)
        {
            _indexes[$"{providerIndex.Hostname}/{providerIndex.Namespace}/{providerIndex.Type}"] = providerIndex;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MirrorProviderPackage>> ListProviderPackagesAsync(string? q, string? state, int limit, int offset)
        {
            return Task.FromResult<IReadOnlyList<MirrorProviderPackage>>(_packages.Values.ToList());
        }

        public Task<MirrorProviderPackage?> GetProviderPackageAsync(
            string hostname,
            string providerNamespace,
            string type,
            string version,
            string os,
            string arch)
        {
            _packages.TryGetValue(Key(hostname, providerNamespace, type, version, os, arch), out var package);
            return Task.FromResult(package);
        }

        public Task UpsertProviderPackageAsync(MirrorProviderPackage package)
        {
            _packages[Key(package.Hostname, package.Namespace, package.Type, package.Version, package.Os, package.Arch)] = package;
            return Task.CompletedTask;
        }

        public Task MarkProviderPackageFailedAsync(
            string hostname,
            string providerNamespace,
            string type,
            string version,
            string os,
            string arch,
            string errorMessage,
            int? httpStatusCode = null)
        {
            var key = Key(hostname, providerNamespace, type, version, os, arch);
            _packages[key] = new MirrorProviderPackage
            {
                Hostname = hostname,
                Namespace = providerNamespace,
                Type = type,
                Version = version,
                Os = os,
                Arch = arch,
                DownloadUrl = $"https://registry.terraform.io/v1/providers/{providerNamespace}/{type}/{version}/download/{os}/{arch}",
                State = "failed",
                LastError = errorMessage,
                HttpStatusCode = httpStatusCode
            };
            return Task.CompletedTask;
        }

        private static string Key(string hostname, string providerNamespace, string type, string version, string os, string arch) =>
            $"{hostname}/{providerNamespace}/{type}/{version}/{os}/{arch}";
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TerraformRegistry.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
