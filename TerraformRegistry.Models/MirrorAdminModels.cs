using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class MirrorConfigResponse
{
    [JsonIgnore]
    public MirrorOptions Effective { get; set; } = new();
    [JsonPropertyName("effective")]
    public MirrorOperatorOptions OperatorEffective { get; set; } = new();
    public bool HasRuntimeOverride { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class MirrorOperatorOptions
{
    public bool Enabled { get; init; }
    public string UpstreamRegistryBaseUrl { get; init; } = string.Empty;
    public MirrorOperatorProviderOptions Providers { get; init; } = new();
    public MirrorModuleRuntimeOptions Modules { get; init; } = new();
    public MirrorLimitRuntimeOptions Limits { get; init; } = new();

    public static MirrorOperatorOptions From(MirrorOptions options) => new()
    {
        Enabled = options.Enabled,
        UpstreamRegistryBaseUrl = options.UpstreamRegistryBaseUrl,
        Providers = new MirrorOperatorProviderOptions
        {
            Enabled = options.Providers.Enabled,
            RequireAuthentication = options.Providers.RequireAuthentication,
            AllowedHostnames = [.. options.Providers.AllowedHostnames],
            UpstreamRegistryUrls = new(options.Providers.UpstreamRegistryUrls, StringComparer.OrdinalIgnoreCase),
            AllowedArtifactHosts = [.. options.Providers.AllowedArtifactHosts],
            Allowlist = [.. options.Providers.Allowlist],
            Denylist = [.. options.Providers.Denylist],
            Platforms = [.. options.Providers.Platforms],
            MaxPackageBytes = options.Providers.MaxPackageBytes,
            MaxChecksumBytes = options.Providers.MaxChecksumBytes,
            MaxRedirects = options.Providers.MaxRedirects,
            MetadataTtlMinutes = options.Providers.MetadataTtlMinutes,
            DownloadTimeoutSeconds = options.Providers.DownloadTimeoutSeconds
        },
        Modules = options.Modules,
        Limits = options.Limits
    };
}

public sealed class MirrorOperatorProviderOptions
{
    public bool Enabled { get; init; }
    public bool RequireAuthentication { get; init; }
    public List<string> AllowedHostnames { get; init; } = [];
    public Dictionary<string, string> UpstreamRegistryUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AllowedArtifactHosts { get; init; } = [];
    public List<string> Allowlist { get; init; } = [];
    public List<string> Denylist { get; init; } = [];
    public List<string> Platforms { get; init; } = [];
    public long MaxPackageBytes { get; init; }
    public long MaxChecksumBytes { get; init; }
    public int MaxRedirects { get; init; }
    public int MetadataTtlMinutes { get; init; }
    public int DownloadTimeoutSeconds { get; init; }
}

public sealed class MirrorConfigUpdateRequest
{
    public bool Enabled { get; set; }
    public MirrorProviderRuntimeOptions Providers { get; set; } = new();
    public MirrorModuleRuntimeOptions Modules { get; set; } = new();
    public MirrorLimitRuntimeOptions Limits { get; set; } = new();
}
