namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionRuntimeConfig
{
    public bool Enabled { get; init; }
    public bool StartupEnabled { get; init; }
    public bool? PersistedEnabled { get; init; }
    public bool HasRuntimeOverride { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
