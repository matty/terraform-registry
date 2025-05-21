using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a module with detailed information
/// </summary>
public class Module
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

    [JsonPropertyName("root")] public required string Root { get; set; }

    [JsonPropertyName("submodules")] public required List<ModuleSubmodule> Submodules { get; set; }

    [JsonPropertyName("providers")] public required Dictionary<string, string> Providers { get; set; }

    [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
}