namespace TerraformRegistry.Models;

public class ModuleLlmContextDocument
{
    public string SchemaVersion { get; set; } = "module-llm-context.v1";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string Generator { get; set; } = "module-llm-context-generator";
    public ModuleLlmModuleReference Module { get; set; } = new();
    public ModuleLlmSourceReference? Source { get; set; }
    public ModuleLlmContextSummary Summary { get; set; } = new();
    public List<ModuleInputDefinition> Inputs { get; set; } = [];
    public List<ModuleOutputDefinition> Outputs { get; set; } = [];
    public List<ModuleProviderRequirement> Providers { get; set; } = [];
    public ModuleLlmResourceSummary Resources { get; set; } = new();
    public List<ModuleLlmExampleSummary> Examples { get; set; } = [];
    public ModuleLlmReadmeSummary Readme { get; set; } = new();
    public ModuleLlmNavigationLinks Navigation { get; set; } = new();
    public List<string> Warnings { get; set; } = [];
}

public class ModuleLlmModuleReference
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class ModuleLlmSourceReference
{
    public string? RegistryUrl { get; set; }
    public string? PublishedAt { get; set; }
}

public class ModuleLlmExampleSummary
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Summary { get; set; }
}

public class ModuleLlmReadmeSummary
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
}
