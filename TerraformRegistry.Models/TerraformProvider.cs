using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class TerraformProvider
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("namespace")] public required string Namespace { get; set; }
    [JsonPropertyName("type")] public required string Type { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("source_repository_url")] public string? SourceRepositoryUrl { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("deleted_at")] public DateTime? DeletedAt { get; set; }
}
