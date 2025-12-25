using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
/// Represents a Terraform Provider
/// </summary>
public class Provider
{
    public string Namespace { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string PublishedAt { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = new();
}

/// <summary>
/// Represents the list of available versions for a provider
/// </summary>
public class ProviderVersions
{
    [JsonPropertyName("versions")]
    public List<ProviderVersionInfo> Versions { get; set; } = new();
}

public class ProviderVersionInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("protocols")]
    public List<string> Protocols { get; set; } = new();

    [JsonPropertyName("platforms")]
    public List<PlatformInfo> Platforms { get; set; } = new();
}

public class PlatformInfo
{
    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("arch")]
    public string Arch { get; set; } = string.Empty;
}

/// <summary>
/// Represents the download information for a specific provider platform
/// </summary>
public class ProviderPackage
{
    [JsonPropertyName("protocols")]
    public List<string> Protocols { get; set; } = new();

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("arch")]
    public string Arch { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("shasum")]
    public string Shasum { get; set; } = string.Empty;

    [JsonPropertyName("signing_keys")]
    public SigningKeys SigningKeys { get; set; } = new();
}

public class SigningKeys
{
    [JsonPropertyName("gpg_public_keys")]
    public List<GpgPublicKey> GpgPublicKeys { get; set; } = new();
}

public class GpgPublicKey
{
    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("ascii_armor")]
    public string AsciiArmor { get; set; } = string.Empty;

    [JsonPropertyName("trust_signature")]
    public string TrustSignature { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;
}
