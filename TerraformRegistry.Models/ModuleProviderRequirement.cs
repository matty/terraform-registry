namespace TerraformRegistry.Models;

public class ModuleProviderRequirement
{
    public string Name { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? VersionConstraint { get; set; }
}
