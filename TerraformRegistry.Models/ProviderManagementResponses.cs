using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderManagementVersionsResponse
{
    [JsonPropertyName("versions")] public required List<ProviderManagementVersionEntry> Versions { get; set; }
}

public sealed class ProviderManagementVersionEntry
{
    [JsonPropertyName("id")] public required string Id { get; set; }
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("protocols")] public required string[] Protocols { get; set; }
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
    [JsonPropertyName("has_shasums")] public bool HasShasums { get; set; }
    [JsonPropertyName("has_shasums_signature")] public bool HasShasumsSignature { get; set; }
    [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
    [JsonPropertyName("platforms")] public required List<ProviderManagementPlatformEntry> Platforms { get; set; }
}

public sealed class ProviderManagementPlatformsResponse
{
    [JsonPropertyName("platforms")] public required List<ProviderManagementPlatformEntry> Platforms { get; set; }
}

public sealed class ProviderManagementPlatformEntry
{
    [JsonPropertyName("id")] public required string Id { get; set; }
    [JsonPropertyName("os")] public required string Os { get; set; }
    [JsonPropertyName("arch")] public required string Arch { get; set; }
    [JsonPropertyName("filename")] public required string Filename { get; set; }
    [JsonPropertyName("shasum")] public required string Shasum { get; set; }
    [JsonPropertyName("has_package")] public bool HasPackage { get; set; }
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("uploaded_at")] public DateTime? UploadedAt { get; set; }
}
