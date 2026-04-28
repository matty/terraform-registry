namespace TerraformRegistry.Models;

public class ModuleDocumentationSummary
{
    public string? PrimaryReadmePath { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int ExampleCount { get; set; }
    public bool HasSubmoduleDocs { get; set; }
}
