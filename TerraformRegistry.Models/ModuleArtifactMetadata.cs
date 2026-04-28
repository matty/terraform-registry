namespace TerraformRegistry.Models;

public class ModuleArtifactMetadata
{
    public string SchemaVersion { get; set; } = "module-metadata.v1";
    public ModuleSourceInfo? Source { get; set; }
}
