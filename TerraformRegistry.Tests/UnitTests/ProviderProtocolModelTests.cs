using System.Text.Json;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class ProviderProtocolModelTests
{
    [Fact]
    public void ProviderVersionsResponseSerializesTerraformProviderProtocolShape()
    {
        var response = new ProviderVersionsResponse
        {
            Versions =
            [
                new ProviderVersionEntry
                {
                    Version = "1.0.0",
                    Protocols = ["5.0"],
                    Platforms =
                    [
                        new ProviderPlatformEntry
                        {
                            Os = "linux",
                            Arch = "amd64"
                        }
                    ]
                }
            ]
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        var version = json.RootElement.GetProperty("versions")[0];
        Assert.Equal("1.0.0", version.GetProperty("version").GetString());
        Assert.Equal("5.0", version.GetProperty("protocols")[0].GetString());
        Assert.Equal("linux", version.GetProperty("platforms")[0].GetProperty("os").GetString());
        Assert.Equal("amd64", version.GetProperty("platforms")[0].GetProperty("arch").GetString());
    }

    [Fact]
    public void ProviderPackageResponseSerializesSigningKeysForTerraformCli()
    {
        var response = new ProviderPackageResponse
        {
            Protocols = ["5.0"],
            Os = "linux",
            Arch = "amd64",
            Filename = "terraform-provider-example_1.0.0_linux_amd64.zip",
            DownloadUrl = "https://registry.example.test/assets/package.zip",
            ShasumsUrl = "https://registry.example.test/assets/SHA256SUMS",
            ShasumsSignatureUrl = "https://registry.example.test/assets/SHA256SUMS.sig",
            Shasum = "a".PadLeft(64, 'a'),
            SigningKeys = new ProviderSigningKeys
            {
                GpgPublicKeys =
                [
                    new ProviderGpgPublicKey
                    {
                        KeyId = "ABC123",
                        AsciiArmor = "-----BEGIN PGP PUBLIC KEY BLOCK-----",
                        TrustSignature = "trust",
                        Source = "user",
                        SourceUrl = "https://registry.example.test/keys/ABC123"
                    }
                ]
            }
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal("linux", json.RootElement.GetProperty("os").GetString());
        Assert.Equal("amd64", json.RootElement.GetProperty("arch").GetString());
        Assert.Equal("terraform-provider-example_1.0.0_linux_amd64.zip",
            json.RootElement.GetProperty("filename").GetString());
        Assert.True(json.RootElement.TryGetProperty("download_url", out _));
        Assert.True(json.RootElement.TryGetProperty("shasums_url", out _));
        Assert.True(json.RootElement.TryGetProperty("shasums_signature_url", out _));
        Assert.Equal("ABC123", json.RootElement
            .GetProperty("signing_keys")
            .GetProperty("gpg_public_keys")[0]
            .GetProperty("key_id")
            .GetString());
    }
}
