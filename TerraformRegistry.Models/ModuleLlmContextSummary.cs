namespace TerraformRegistry.Models;

public class ModuleLlmContextSummary
{
    public string? OneLine { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public List<string> UsageNotes { get; set; } = [];
}
