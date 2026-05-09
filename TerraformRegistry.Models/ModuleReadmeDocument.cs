namespace TerraformRegistry.Models;

public class ModuleReadmeDocument
{
    public string Path { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Markdown { get; set; } = string.Empty;
}
