namespace TerraformRegistry.Models;

public sealed class MirrorOptions
{
    public bool Enabled { get; set; }
    public string UpstreamRegistryBaseUrl { get; set; } = "https://registry.terraform.io";
    public MirrorProviderRuntimeOptions Providers { get; set; } = new();
    public MirrorModuleRuntimeOptions Modules { get; set; } = new();
    public MirrorLimitRuntimeOptions Limits { get; set; } = new();
}

public sealed class MirrorProviderRuntimeOptions
{
    public bool Enabled { get; set; } = true;
    public bool RequireAuthentication { get; set; } = true;
    public List<string> AllowedHostnames { get; set; } = ["registry.terraform.io"];
    public List<string> Allowlist { get; set; } = [];
    public List<string> Denylist { get; set; } = [];
    public List<string> Platforms { get; set; } = [];
    public long MaxPackageBytes { get; set; } = 524_288_000;
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
