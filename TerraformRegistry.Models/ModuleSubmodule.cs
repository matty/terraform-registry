namespace TerraformRegistry.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a submodule within a module
/// </summary>
public class ModuleSubmodule
{
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    [JsonPropertyName("providers")]
    public required Dictionary<string, string> Providers { get; set; }
}