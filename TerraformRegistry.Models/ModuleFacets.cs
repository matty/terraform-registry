using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Distinct values available for filtering the module catalog.
/// </summary>
public class ModuleFacets
{
    [JsonPropertyName("namespaces")] public required List<string> Namespaces { get; set; }

    [JsonPropertyName("providers")] public required List<string> Providers { get; set; }
}
