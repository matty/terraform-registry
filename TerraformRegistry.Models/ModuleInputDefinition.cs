namespace TerraformRegistry.Models;

public class ModuleInputDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; }
    public string? Type { get; set; }
    public string? DefaultJson { get; set; }
    public bool Sensitive { get; set; }
}
