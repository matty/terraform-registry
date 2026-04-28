namespace TerraformRegistry.Models;

public class ModuleExampleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ReadmePath { get; set; }
}
