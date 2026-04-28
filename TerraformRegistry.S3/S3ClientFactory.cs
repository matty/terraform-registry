using Amazon.Runtime;
using Amazon.S3;

namespace TerraformRegistry.S3;

public sealed class S3ClientFactory : IS3ClientFactory
{
    public IAmazonS3 Create(
        AmazonS3Config config,
        string? accessKeyId,
        string? secretAccessKey,
        string? sessionToken)
    {
        if (!string.IsNullOrWhiteSpace(accessKeyId) && !string.IsNullOrWhiteSpace(secretAccessKey))
        {
            AWSCredentials credentials = string.IsNullOrWhiteSpace(sessionToken)
                ? new BasicAWSCredentials(accessKeyId, secretAccessKey)
                : new SessionAWSCredentials(accessKeyId, secretAccessKey, sessionToken);

            return new AmazonS3Client(credentials, config);
        }

        return new AmazonS3Client(config);
    }
}
