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

public sealed record MirrorAdminSummaryResponse(
    MirrorAdminCacheSummary Providers,
    MirrorAdminCacheSummary Modules,
    MirrorAdminCacheSummary Total);

public sealed record MirrorAdminCacheSummary(
    int Total,
    int Ready,
    int Pending,
    int Failed,
    DateTime? LastSyncAt);

public sealed record MirrorAdminEntriesResponse(
    IReadOnlyList<MirrorProviderPackage> Providers,
    IReadOnlyList<MirrorModulePackage> Modules,
    int Limit,
    int Offset);

public sealed class MirrorAdminRetryRequest
{
    public string Kind { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public string? Type { get; set; }
    public string Version { get; set; } = string.Empty;
}
