namespace TerraformRegistry.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a response for module versions
/// </summary>
public class ModuleVersions
{
    [JsonPropertyName("versions")]
    public required List<string> Versions { get; set; }
}