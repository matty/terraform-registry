namespace TerraformRegistry.Models;

public class ModuleSourceInfo
{
    public string Kind { get; set; } = "api-upload";
    public string? RepoUrl { get; set; }
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? Ref { get; set; }
}
