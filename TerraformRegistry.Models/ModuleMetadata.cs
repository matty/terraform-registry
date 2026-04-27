using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents metadata for a Terraform module
/// </summary>
public class ModuleMetadata
{
    /// <summary>
    ///     Description of the module
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Root path for the module within the package
    /// </summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    /// <summary>
    ///     Provider constraints keyed by provider name
    /// </summary>
    [JsonPropertyName("providers")]
    public Dictionary<string, string>? Providers { get; set; }

    /// <summary>
    ///     Submodules discovered or declared for the module
    /// </summary>
    [JsonPropertyName("submodules")]
    public List<ModuleSubmodule>? Submodules { get; set; }

    /// <summary>
    ///     Additional properties for module metadata
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string>? Properties { get; set; }
}
