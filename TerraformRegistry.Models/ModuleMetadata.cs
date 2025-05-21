namespace TerraformRegistry.Models;

/// <summary>
///     Represents metadata for a Terraform module
/// </summary>
public class ModuleMetadata
{
    /// <summary>
    ///     Description of the module
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Additional properties for module metadata
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }
}