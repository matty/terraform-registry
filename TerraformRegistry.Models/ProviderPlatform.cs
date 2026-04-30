using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderPlatform
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("provider_version_id")] public Guid ProviderVersionId { get; set; }
    [JsonPropertyName("os")] public required string Os { get; set; }
    [JsonPropertyName("arch")] public required string Arch { get; set; }
    [JsonPropertyName("filename")] public required string Filename { get; set; }
    [JsonPropertyName("shasum")] public required string Shasum { get; set; }
    [JsonPropertyName("package_storage_path")] public string? PackageStoragePath { get; set; }
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("uploaded_at")] public DateTime? UploadedAt { get; set; }
}
