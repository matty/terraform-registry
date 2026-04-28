namespace TerraformRegistry.Models;

public class ModuleArtifactMetadata
{
    public string SchemaVersion { get; set; } = "module-metadata.v1";
    public string? RootSubdirectory { get; set; }
    public ModuleSourceInfo? Source { get; set; }
    public List<ModuleProviderRequirement> ProviderRequirements { get; set; } = [];
    public List<ModuleSubmodule> Submodules { get; set; } = [];
    public ModuleDocumentationSummary? Documentation { get; set; }
    public ModuleExtractionState Extraction { get; set; } = new() { Status = "pending" };
}
