namespace TerraformRegistry.Models;

public class ModuleResourceDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Mode { get; set; }
}
