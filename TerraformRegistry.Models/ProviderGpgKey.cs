using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class ProviderGpgKey
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("namespace")] public required string Namespace { get; set; }
    [JsonPropertyName("key_id")] public required string KeyId { get; set; }
    [JsonPropertyName("ascii_armor")] public required string AsciiArmor { get; set; }
    [JsonPropertyName("trust_signature")] public string? TrustSignature { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("revoked_at")] public DateTime? RevokedAt { get; set; }
}
