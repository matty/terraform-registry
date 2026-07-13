using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

public sealed class MirrorOptions
{
    public bool Enabled { get; set; }
    public string UpstreamRegistryBaseUrl { get; set; } = "https://registry.terraform.io";
    [JsonIgnore]
    public string? PackageUrlSigningKey { get; set; }
    public MirrorProviderRuntimeOptions Providers { get; set; } = new();
    public MirrorModuleRuntimeOptions Modules { get; set; } = new();
    public MirrorLimitRuntimeOptions Limits { get; set; } = new();
}

public sealed class MirrorProviderRuntimeOptions
{
    public bool Enabled { get; set; } = true;
    public bool RequireAuthentication { get; set; } = true;
    public List<string> AllowedHostnames { get; set; } = ["registry.terraform.io"];
    /// <summary>
    /// Maps each advertised provider mirror hostname to the HTTPS registry used to retrieve it.
    /// A multi-host configuration must provide one explicit entry for every allowed hostname.
    /// </summary>
    public Dictionary<string, string> UpstreamRegistryUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// GPG key IDs that are trusted to sign mirrored provider SHA256SUMS files.
    /// Packages signed by any other key fail closed.
    /// </summary>
    public List<string> TrustedSigningKeyIds { get; set; } = [];
    public List<string> AllowedArtifactHosts { get; set; } = [];
    public List<string> Allowlist { get; set; } = [];
    public List<string> Denylist { get; set; } = [];
    public List<string> Platforms { get; set; } = [];
    public long MaxPackageBytes { get; set; } = 524_288_000;
    public long MaxChecksumBytes { get; set; } = 5_242_880;
    public int MaxRedirects { get; set; } = 3;
    public int MetadataTtlMinutes { get; set; } = 60;
    public int DownloadTimeoutSeconds { get; set; } = 120;
}

public sealed class MirrorModuleRuntimeOptions
{
    public bool Enabled { get; set; } = true;
    public bool RequireAuthentication { get; set; } = true;
    public List<string> AllowedNamespaces { get; set; } = [];
    public List<string> AllowedArchiveHosts { get; set; } = ["github.com", "codeload.github.com"];
    public List<string> Allowlist { get; set; } = [];
    public List<string> Denylist { get; set; } = [];
    public long MaxPackageBytes { get; set; } = 104_857_600;
    public int MaxRedirects { get; set; } = 3;
    public int MetadataTtlMinutes { get; set; } = 60;
    public int DownloadTimeoutSeconds { get; set; } = 120;
}

public sealed class MirrorLimitRuntimeOptions
{
    public int MaxConcurrentDownloads { get; set; } = 4;
    public int MaxConcurrentDownloadsPerCoordinate { get; set; } = 1;
    public long MaxTotalCachedBytes { get; set; } = 107_374_182_400;
    public int NegativeCacheTtlSeconds { get; set; } = 60;
}
