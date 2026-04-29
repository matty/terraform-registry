namespace TerraformRegistry.Models;

public sealed class ModuleLlmIndexResponse
{
    public string SchemaVersion { get; set; } = "registry-llm-index.v1";
    public ModuleLlmRegistryInfo Registry { get; set; } = new();
    public List<ModuleLlmIndexItem> Modules { get; set; } = [];
    public ModuleLlmPagination Pagination { get; set; } = new();
}

public sealed class ModuleLlmRegistryInfo
{
    public string Name { get; set; } = "Terraform Registry";
    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class ModuleLlmIndexItem
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string VersionsUrl { get; set; } = string.Empty;
    public string ContextUrl { get; set; } = string.Empty;
}

public sealed class ModuleLlmPagination
{
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Returned { get; set; }
    public string? Next { get; set; }
}

public sealed class ModuleLlmModuleVersionsResponse
{
    public string SchemaVersion { get; set; } = "registry-llm-module.v1";
    public ModuleLlmModuleReference Module { get; set; } = new();
    public List<ModuleLlmVersionItem> Versions { get; set; } = [];
}

public sealed class ModuleLlmVersionItem
{
    public string Version { get; set; } = string.Empty;
    public bool LlmReady { get; set; }
    public string ContextUrl { get; set; } = string.Empty;
}
