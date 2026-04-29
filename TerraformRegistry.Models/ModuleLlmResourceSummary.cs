namespace TerraformRegistry.Models;

public class ModuleLlmResourceSummary
{
    public List<string> Managed { get; set; } = [];
    public List<string> Data { get; set; } = [];
}
