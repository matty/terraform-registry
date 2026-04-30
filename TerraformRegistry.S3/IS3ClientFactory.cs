using Amazon.S3;

namespace TerraformRegistry.S3;

public interface IS3ClientFactory
{
    IAmazonS3 Create(
        AmazonS3Config config,
        string? accessKeyId,
        string? secretAccessKey,
        string? sessionToken);
}
