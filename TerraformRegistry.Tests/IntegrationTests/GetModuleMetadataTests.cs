using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class GetModuleMetadataTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    private const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Upload_ModuleWithManifestMetadata_GetModule_ReturnsManifestMetadata()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        using var content = new MultipartFormDataContent();
        var archiveBytes = CreateModuleArchiveWithManifest();
        var streamContent = new ByteArrayContent(archiveBytes);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(streamContent, "moduleFile", "manifest-module.zip");
        content.Add(new StringContent(string.Empty), "description");

        var uploadResponse = await client.PostAsync("/v1/modules/test-ns/manifest-module/aws/1.0.0", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var getResponse = await client.GetAsync("/v1/modules/test-ns/manifest-module/aws/1.0.0");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var json = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("modules/network", root.GetProperty("root").GetString());

        var providers = root.GetProperty("providers");
        Assert.Equal("~> 5.0", providers.GetProperty("aws").GetString());
        Assert.Equal(">= 3.0", providers.GetProperty("random").GetString());

        var submodule = Assert.Single(root.GetProperty("submodules").EnumerateArray());
        Assert.Equal("modules/network", submodule.GetProperty("path").GetString());
        Assert.Equal("~> 5.0", submodule.GetProperty("providers").GetProperty("aws").GetString());
    }

    private static byte[] CreateModuleArchiveWithManifest()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mainTf = archive.CreateEntry("main.tf");
            using (var writer = new StreamWriter(mainTf.Open(), Encoding.UTF8, leaveOpen: false))
            {
                writer.WriteLine("terraform {}");
            }

            var manifest = archive.CreateEntry("module.json");
            using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(
                    """
                    {
                      "description": "Manifest-backed module",
                      "root": "modules/network",
                      "providers": {
                        "aws": "~> 5.0",
                        "random": ">= 3.0"
                      },
                      "submodules": [
                        {
                          "path": "modules/network",
                          "providers": {
                            "aws": "~> 5.0"
                          }
                        }
                      ]
                    }
                    """);
            }
        }

        return memory.ToArray();
    }
}
