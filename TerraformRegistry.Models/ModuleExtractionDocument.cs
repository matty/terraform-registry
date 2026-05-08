namespace TerraformRegistry.Models;

public class ModuleExtractionDocument
{
    public string SchemaVersion { get; set; } = "module-extraction.v1";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string Extractor { get; set; } = "terraform-config-inspect";
    public ModuleReadmeDocument? Readme { get; set; }
    public List<ModuleInputDefinition> Inputs { get; set; } = [];
    public List<ModuleOutputDefinition> Outputs { get; set; } = [];
    public List<ModuleProviderRequirement> ProviderRequirements { get; set; } = [];
    public List<ModuleResourceDefinition> ManagedResources { get; set; } = [];
    public List<ModuleResourceDefinition> DataResources { get; set; } = [];
    public List<ModuleSubmodule> Submodules { get; set; } = [];
    public List<ModuleExampleDefinition> Examples { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
