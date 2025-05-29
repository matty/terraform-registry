using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

/// <summary>
///     Represents a module version in the versions response
/// </summary>
public class VersionInfo
{
    [JsonPropertyName("version")] public required string Version { get; set; }
}

/// <summary>
///     Represents a module in the versions response
/// </summary>
public class ModuleVersionInfo
{
    [JsonPropertyName("versions")] public required List<VersionInfo> Versions { get; set; }
}

/// <summary>
///     Represents the response structure for module versions
///     Format follows Terraform Registry protocol specification
/// </summary>
public class ModuleVersions
{
    [JsonPropertyName("modules")] public required List<ModuleVersionInfo> Modules { get; set; } = new();
}