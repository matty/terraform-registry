using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TerraformRegistry.S3;

namespace TerraformRegistry.Tests.UnitTests.S3;

public class S3ProviderArtifactStorageTests
{
    private readonly Mock<IAmazonS3> _s3Client = new();

    [Fact]
    public async Task SaveAsyncStoresArtifactUnderProvidersPrefixAndReturnsRelativeStoragePath()
    {
        PutObjectRequest? capturedRequest = null;
        _s3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == "registry-artifacts" &&
                                                     r.Key == "providers/acme/example/1.0.0/package.zip"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 3 });

        var storage = CreateStorage();
        await using var content = new MemoryStream([1, 2, 3]);

        var result = await storage.SaveAsync("acme/example/1.0.0/package.zip", content, CancellationToken.None);

        Assert.Equal("acme/example/1.0.0/package.zip", result.StoragePath);
        Assert.Equal(3, result.SizeBytes);
        Assert.NotNull(capturedRequest);
        Assert.Equal("registry-artifacts", capturedRequest!.BucketName);
        Assert.Equal("providers/acme/example/1.0.0/package.zip", capturedRequest.Key);
        Assert.False(capturedRequest.AutoCloseStream);
    }

    [Fact]
    public async Task SaveAsyncSetsContentLengthForNonSeekableRequestStream()
    {
        PutObjectRequest? capturedRequest = null;
        _s3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 3 });

        var storage = CreateStorage();
        await using var content = new NonSeekableReadStream([1, 2, 3]);

        await storage.SaveAsync(
            "acme/example/1.0.0/terraform-provider-example_1.0.0_SHA256SUMS",
            content,
            contentLength: 3,
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(3, capturedRequest!.Headers.ContentLength);
        Assert.False(capturedRequest.AutoResetStreamPosition);
    }

    [Fact]
    public async Task CreateDownloadUrlAsyncReturnsPresignedUrlForStoredArtifact()
    {
        const string expectedUrl = "https://example.invalid/provider.zip";
        GetPreSignedUrlRequest? capturedRequest = null;
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == "registry-artifacts" &&
                                                     r.Key == "providers/acme/example/1.0.0/package.zip"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());
        _s3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Callback<GetPreSignedUrlRequest>(request => capturedRequest = request)
            .Returns(expectedUrl);

        var storage = CreateStorage();
        var beforeCall = DateTime.UtcNow;

        var result = await storage.CreateDownloadUrlAsync("acme/example/1.0.0/package.zip", CancellationToken.None);
        var afterCall = DateTime.UtcNow;

        Assert.Equal(expectedUrl, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("registry-artifacts", capturedRequest!.BucketName);
        Assert.Equal("providers/acme/example/1.0.0/package.zip", capturedRequest.Key);
        Assert.Equal(HttpVerb.GET, capturedRequest.Verb);
        Assert.Equal(Protocol.HTTPS, capturedRequest.Protocol);
        Assert.NotNull(capturedRequest.Expires);
        Assert.InRange(capturedRequest.Expires!.Value, beforeCall.AddMinutes(17), afterCall.AddMinutes(17));
    }

    [Fact]
    public async Task CreateDownloadUrlAsyncThrowsFileNotFoundWhenArtifactIsMissing()
    {
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var storage = CreateStorage();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.CreateDownloadUrlAsync("acme/example/1.0.0/package.zip", CancellationToken.None));
        _s3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }

    [Fact]
    public async Task OpenReadAsyncReturnsObjectResponseStream()
    {
        _s3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.BucketName == "registry-artifacts" &&
                                            r.Key == "providers/acme/example/1.0.0/package.zip"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([4, 5, 6]) });

        var storage = CreateStorage();

        await using var result = await storage.OpenReadAsync("acme/example/1.0.0/package.zip", CancellationToken.None);

        Assert.NotNull(result);
        await using var copy = new MemoryStream();
        await result!.CopyToAsync(copy);
        Assert.Equal([4, 5, 6], copy.ToArray());
    }

    [Fact]
    public async Task OpenReadAsyncReturnsNullWhenObjectIsMissing()
    {
        _s3Client
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var storage = CreateStorage();

        var result = await storage.OpenReadAsync("acme/example/1.0.0/package.zip", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenMetadataExists()
    {
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        var storage = CreateStorage();

        Assert.True(await storage.ExistsAsync("acme/example/1.0.0/package.zip", CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenMetadataIsMissing()
    {
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var storage = CreateStorage();

        Assert.False(await storage.ExistsAsync("acme/example/1.0.0/package.zip", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsyncRemovesObjectFromProvidersPrefix()
    {
        DeleteObjectRequest? capturedRequest = null;
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        _s3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

        var storage = CreateStorage();

        Assert.True(await storage.DeleteAsync("acme/example/1.0.0/package.zip", CancellationToken.None));
        Assert.NotNull(capturedRequest);
        Assert.Equal("registry-artifacts", capturedRequest!.BucketName);
        Assert.Equal("providers/acme/example/1.0.0/package.zip", capturedRequest.Key);
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalseWhenObjectIsMissing()
    {
        _s3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var storage = CreateStorage();

        Assert.False(await storage.DeleteAsync("acme/example/1.0.0/package.zip", CancellationToken.None));
        _s3Client.Verify(
            x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckStorageAsyncReturnsHealthyWhenProbeWriteReadAndDeleteSucceed()
    {
        string? probeKey = null;
        _s3Client
            .Setup(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r => r.BucketName == "registry-artifacts" &&
                                             r.Key.StartsWith("providers/.health-", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => probeKey = request.Key)
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.BucketName == "registry-artifacts" &&
                                             r.Key.StartsWith("providers/.health-", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([1]) });
        _s3Client
            .Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r => r.BucketName == "registry-artifacts" &&
                                                r.Key.StartsWith("providers/.health-", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent });

        var storage = CreateStorage();

        var result = await storage.CheckStorageAsync(CancellationToken.None);

        Assert.True(result.Healthy);
        Assert.Null(result.Reason);
        Assert.NotNull(probeKey);
        _s3Client.Verify(x => x.GetObjectAsync(
            It.Is<GetObjectRequest>(r => r.Key == probeKey),
            It.IsAny<CancellationToken>()), Times.Once);
        _s3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(r => r.Key == probeKey),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckStorageAsyncReturnsUnhealthyWhenProbeWriteFails()
    {
        _s3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("put denied"));

        var storage = CreateStorage();

        var result = await storage.CheckStorageAsync(CancellationToken.None);

        Assert.False(result.Healthy);
        Assert.Contains("S3 provider artifact storage unreachable", result.Reason);
        Assert.Contains("put denied", result.Reason);
    }

    [Theory]
    [InlineData("../outside.zip")]
    [InlineData("/absolute.zip")]
    [InlineData("acme/../outside.zip")]
    [InlineData("")]
    public async Task SaveAsyncRejectsPathsOutsideProviderPrefix(string storagePath)
    {
        var storage = CreateStorage();
        await using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(storagePath, content, CancellationToken.None));
    }

    private S3ProviderArtifactStorage CreateStorage()
    {
        return new S3ProviderArtifactStorage(
            CreateConfiguration(),
            NullLogger<S3ProviderArtifactStorage>.Instance,
            _s3Client.Object);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
(StringComparer.Ordinal)
            {
                ["S3:BucketName"] = "registry-artifacts",
                ["S3:Region"] = "eu-west-2",
                ["S3:PresignedUrlExpiryMinutes"] = "17"
            })
            .Build();
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
