using System.Globalization;
using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.S3;

public sealed partial class S3ProviderArtifactStorage : IProviderArtifactStorage
{
    private readonly string _bucketName;
    private readonly ILogger<S3ProviderArtifactStorage> _logger;
    private readonly int _presignedUrlExpiryMinutes;
    private readonly IAmazonS3 _s3Client;
    private readonly bool _useHttp;

    public S3ProviderArtifactStorage(
        IConfiguration configuration,
        ILogger<S3ProviderArtifactStorage> logger,
        IAmazonS3? s3Client = null,
        IS3ClientFactory? s3ClientFactory = null)
    {
        _logger = logger;

        var bucketName = configuration["S3:BucketName"];
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException("S3:BucketName is required.");
        }

        _bucketName = bucketName;
        _useHttp = Uri.TryCreate(configuration["S3:ServiceUrl"], UriKind.Absolute, out var configuredEndpoint) &&
                   string.Equals(configuredEndpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        var region = configuration["S3:Region"];
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException("S3:Region is required.");
        }

        var configuredPresignedUrlExpiry = configuration["S3:PresignedUrlExpiryMinutes"] ?? "5";
        if (!int.TryParse(configuredPresignedUrlExpiry, CultureInfo.InvariantCulture, out _presignedUrlExpiryMinutes)
            || _presignedUrlExpiryMinutes <= 0)
        {
            LogInvalidPresignedUrlExpiry(_logger, configuredPresignedUrlExpiry);
            _presignedUrlExpiryMinutes = 5;
        }

        if (s3Client != null)
        {
            _s3Client = s3Client;
            return;
        }

        var config = new AmazonS3Config
        {
            AuthenticationRegion = region,
            RegionEndpoint = RegionEndpoint.GetBySystemName(region),
            ForcePathStyle = bool.TryParse(configuration["S3:ForcePathStyle"], out var forcePathStyle) &&
                             forcePathStyle
        };

        var serviceUrl = configuration["S3:ServiceUrl"];
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.UseHttp = _useHttp;
        }

        _s3Client = (s3ClientFactory ?? new S3ClientFactory()).Create(
            config,
            configuration["S3:AccessKeyId"],
            configuration["S3:SecretAccessKey"],
            configuration["S3:SessionToken"]);
    }

    public async Task<ProviderArtifactSaveResult> SaveAsync(string relativePath, Stream content,
        CancellationToken cancellationToken)
    {
        var objectKey = GetObjectKey(relativePath);

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false
        }, cancellationToken);

        var metadata = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucketName,
            Key = objectKey
        }, cancellationToken);

        return new ProviderArtifactSaveResult(GetStoragePath(objectKey), metadata.ContentLength);
    }

    public Task<string> CreateDownloadUrlAsync(string storagePath, CancellationToken cancellationToken)
    {
        return CreateDownloadUrlAsyncInternal(storagePath, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        return OpenReadAsyncInternal(storagePath, cancellationToken);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken)
    {
        return ExistsAsyncInternal(storagePath, cancellationToken);
    }

    public Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        return DeleteAsyncInternal(storagePath, cancellationToken);
    }

    public Task<(bool Healthy, string? Reason)> CheckStorageAsync(CancellationToken cancellationToken)
    {
        return CheckStorageAsyncInternal(cancellationToken);
    }

    private async Task<string> CreateDownloadUrlAsyncInternal(string storagePath, CancellationToken cancellationToken)
    {
        var objectKey = GetObjectKey(storagePath);

        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Provider artifact object was not found.", storagePath, ex);
        }

        return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = _useHttp ? Protocol.HTTP : Protocol.HTTPS,
            Expires = DateTime.UtcNow.AddMinutes(_presignedUrlExpiryMinutes)
        });
    }

    private async Task<Stream?> OpenReadAsyncInternal(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = GetObjectKey(storagePath)
            }, cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> ExistsAsyncInternal(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = GetObjectKey(storagePath)
            }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> DeleteAsyncInternal(string storagePath, CancellationToken cancellationToken)
    {
        var objectKey = GetObjectKey(storagePath);

        try
        {
            await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            }, cancellationToken);

            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<(bool Healthy, string? Reason)> CheckStorageAsyncInternal(CancellationToken cancellationToken)
    {
        var probeKey = $"providers/.health-{Guid.NewGuid():N}";
        var probeCreated = false;
        Exception? failure = null;

        try
        {
            await using var content = new MemoryStream([1]);
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = probeKey,
                InputStream = content,
                AutoCloseStream = false
            }, cancellationToken);
            probeCreated = true;

            using var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = probeKey
            }, cancellationToken);

            if (response.ResponseStream == null)
            {
                throw new InvalidOperationException("S3 provider artifact storage health probe returned no content stream.");
            }

            var buffer = new byte[1];
            var bytesRead = await response.ResponseStream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (bytesRead != 1 || buffer[0] != 1)
            {
                throw new InvalidOperationException("S3 provider artifact storage health probe read unexpected content.");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (probeCreated)
        {
            try
            {
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = probeKey
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure == null)
        {
            return (true, null);
        }

        LogStorageHealthCheckFailed(_logger, failure);
        return (false, $"S3 provider artifact storage unreachable: {failure.Message}");
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "S3:PresignedUrlExpiryMinutes must be a positive integer, but was configured as {ConfiguredValue}. Defaulting to 5 minutes.")]
    private static partial void LogInvalidPresignedUrlExpiry(ILogger logger, string configuredValue);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Provider artifact S3 storage health check failed")]
    private static partial void LogStorageHealthCheckFailed(ILogger logger, Exception exception);

    private static string GetObjectKey(string storagePath)
    {
        var normalized = ValidateStoragePath(storagePath);
        return $"providers/{normalized}";
    }

    private static string GetStoragePath(string objectKey)
    {
        const string prefix = "providers/";
        return objectKey.StartsWith(prefix, StringComparison.Ordinal)
            ? objectKey[prefix.Length..]
            : objectKey;
    }

    private static string ValidateStoragePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
        {
            throw new InvalidOperationException("Provider artifact path escapes storage root.");
        }

        var segments = storagePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("Provider artifact path escapes storage root.");
        }

        return string.Join('/', segments);
    }
}
