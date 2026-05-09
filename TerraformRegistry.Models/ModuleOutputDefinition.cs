namespace TerraformRegistry.Models;

public class ModuleOutputDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Sensitive { get; set; }
}
