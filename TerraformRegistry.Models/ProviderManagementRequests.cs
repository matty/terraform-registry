using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class CreateProviderRequest
{
    [JsonPropertyName("namespace")] public required string Namespace { get; set; }
    [JsonPropertyName("type")] public required string Type { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("source_repository_url")] public string? SourceRepositoryUrl { get; set; }
}

public sealed class UpdateProviderRequest
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("source_repository_url")] public string? SourceRepositoryUrl { get; set; }
}

public sealed class CreateProviderVersionRequest
{
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("protocols")] public required string[] Protocols { get; set; }
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
}

public sealed class CreateProviderPlatformRequest
{
    [JsonPropertyName("os")] public required string Os { get; set; }
    [JsonPropertyName("arch")] public required string Arch { get; set; }
    [JsonPropertyName("filename")] public required string Filename { get; set; }
    [JsonPropertyName("shasum")] public required string Shasum { get; set; }
}

public sealed class CreateProviderGpgKeyRequest
{
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
    [JsonPropertyName("ascii_armor")] public required string AsciiArmor { get; set; }
    [JsonPropertyName("trust_signature")] public string? TrustSignature { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
}
