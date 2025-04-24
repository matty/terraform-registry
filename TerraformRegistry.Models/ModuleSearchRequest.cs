namespace TerraformRegistry.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a request to search for modules
/// </summary>
public class ModuleSearchRequest
{
    [JsonPropertyName("q")]
    public string? Q { get; set; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; } = 0;

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 10;
}