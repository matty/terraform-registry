using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a response for module listing
/// </summary>
public class ModuleList
{
    [JsonPropertyName("modules")] public required List<ModuleListItem> Modules { get; set; }

    [JsonPropertyName("meta")] public required Dictionary<string, string> Meta { get; set; }
}
