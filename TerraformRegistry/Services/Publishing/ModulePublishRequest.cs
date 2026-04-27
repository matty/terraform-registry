using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Publishing;

public sealed class ModulePublishRequest
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Version { get; init; }
    public required Stream ModuleContent { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool Replace { get; init; }
    public string? ActorUserId { get; init; }
    public string AuditAction { get; init; } = "module.published";
    public required ModuleArtifactMetadata Metadata { get; init; }
}
