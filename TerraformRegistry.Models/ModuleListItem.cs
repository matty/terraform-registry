using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a single module in a listing
/// </summary>
public class ModuleListItem
{
    [JsonPropertyName("id")] public required string Id { get; set; }

    [JsonPropertyName("owner")] public required string Owner { get; set; }

    [JsonPropertyName("namespace")] public required string Namespace { get; set; }

    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("version")] public required string Version { get; set; }

    [JsonPropertyName("provider")] public required string Provider { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonPropertyName("published_at")] public required string PublishedAt { get; set; }

    [JsonPropertyName("versions")] public required List<string> Versions { get; set; }

    [JsonPropertyName("download_url")] public required string DownloadUrl { get; set; }
}
