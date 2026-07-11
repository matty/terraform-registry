using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.AzureBlob;

namespace TerraformRegistry.Tests.UnitTests.AzureBlob;

public class AzureBlobProviderArtifactStorageTests
{
    [Fact]
    public async Task CreateDownloadUrlAsyncUsesSharedKeySasWhenAvailable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureStorage:ContainerName"] = "artifacts",
            ["AzureStorage:SasTokenExpiryMinutes"] = "5"
        }).Build();
        var serviceClient = new Mock<BlobServiceClient>();
        var containerClient = new Mock<BlobContainerClient>();
        var blobClient = new Mock<BlobClient>();
        var logger = new Mock<ILogger<AzureBlobProviderArtifactStorage>>();
        logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        serviceClient.Setup(x => x.GetBlobContainerClient("artifacts")).Returns(containerClient.Object);
        containerClient.Setup(x => x.CreateIfNotExists(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), default))
            .Returns(Mock.Of<Response<Azure.Storage.Blobs.Models.BlobContainerInfo>>());
        containerClient.Setup(x => x.GetBlobClient("providers/linux/amd64/provider.zip")).Returns(blobClient.Object);
        blobClient.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient.SetupGet(x => x.CanGenerateSasUri).Returns(true);
        blobClient.Setup(x => x.GenerateSasUri(It.IsAny<BlobSasBuilder>()))
            .Returns(new Uri("https://example.test/providers/linux/amd64/provider.zip?sig=test"));

        var storage = new AzureBlobProviderArtifactStorage(configuration, logger.Object, serviceClient.Object);

        var url = await storage.CreateDownloadUrlAsync("linux/amd64/provider.zip", CancellationToken.None);

        Assert.Equal("https://example.test/providers/linux/amd64/provider.zip?sig=test", url);
        blobClient.Verify(x => x.GenerateSasUri(It.Is<BlobSasBuilder>(builder =>
            builder.BlobContainerName == "artifacts" &&
            builder.BlobName == "providers/linux/amd64/provider.zip" &&
            builder.Permissions.Contains('r'))), Times.Once);
    }
}
