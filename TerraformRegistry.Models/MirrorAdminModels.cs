namespace TerraformRegistry.Models;

public sealed class MirrorConfigResponse
{
    public required MirrorOptions Effective { get; set; }
    public bool HasRuntimeOverride { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class MirrorConfigUpdateRequest
{
    public bool Enabled { get; set; }
    public MirrorProviderRuntimeOptions Providers { get; set; } = new();
    public MirrorModuleRuntimeOptions Modules { get; set; } = new();
    public MirrorLimitRuntimeOptions Limits { get; set; } = new();
}
