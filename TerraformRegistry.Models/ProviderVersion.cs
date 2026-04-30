using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderVersion
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("provider_id")] public Guid ProviderId { get; set; }
    [JsonPropertyName("version")] public required string Version { get; set; }
    [JsonPropertyName("protocols")] public required string[] Protocols { get; set; }
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
    [JsonPropertyName("shasums_storage_path")] public string? ShasumsStoragePath { get; set; }
    [JsonPropertyName("shasums_signature_storage_path")] public string? ShasumsSignatureStoragePath { get; set; }
    [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
    [JsonPropertyName("deleted_at")] public DateTime? DeletedAt { get; set; }
}
