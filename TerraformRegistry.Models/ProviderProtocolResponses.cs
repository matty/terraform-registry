using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderVersionsResponse
{
    [JsonPropertyName("versions")] public required List<ProviderVersionEntry> Versions { get; set; }
}

public sealed class ProviderVersionEntry
{
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("protocols")] public required string[] Protocols { get; set; }
    [JsonPropertyName("platforms")] public required List<ProviderPlatformEntry> Platforms { get; set; }
}

public sealed class ProviderPlatformEntry
{
    [JsonPropertyName("os")] public required string Os { get; set; }
    [JsonPropertyName("arch")] public required string Arch { get; set; }
}

public sealed class ProviderPackageResponse
{
    [JsonPropertyName("protocols")] public required string[] Protocols { get; set; }
    [JsonPropertyName("os")] public required string Os { get; set; }
    [JsonPropertyName("arch")] public required string Arch { get; set; }
    [JsonPropertyName("filename")] public required string Filename { get; set; }
    [JsonPropertyName("download_url")] public required string DownloadUrl { get; set; }
    [JsonPropertyName("shasums_url")] public required string ShasumsUrl { get; set; }
    [JsonPropertyName("shasums_signature_url")] public required string ShasumsSignatureUrl { get; set; }
    [JsonPropertyName("shasum")] public required string Shasum { get; set; }
    [JsonPropertyName("signing_keys")] public required ProviderSigningKeys SigningKeys { get; set; }
}

public sealed class ProviderSigningKeys
{
    [JsonPropertyName("gpg_public_keys")] public required List<ProviderGpgPublicKey> GpgPublicKeys { get; set; }
}

public sealed class ProviderGpgPublicKey
{
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
    [JsonPropertyName("ascii_armor")] public required string AsciiArmor { get; set; }
    [JsonPropertyName("trust_signature")] public string? TrustSignature { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
}
