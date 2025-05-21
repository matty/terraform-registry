namespace TerraformRegistry.Models;

/// <summary>
///     Represents a module storage model for internal use
/// </summary>
public class ModuleStorage
{
    // Internal model, no need for JSON attributes
    public required string Namespace { get; set; }
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public required string Version { get; set; }
    public required string Description { get; set; }
    public required string FilePath { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public required List<string> Dependencies { get; set; }
}