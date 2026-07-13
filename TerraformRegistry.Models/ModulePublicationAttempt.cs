namespace TerraformRegistry.Models;

public static class ModulePublicationAttemptState
{
    public const string Staged = "staged";
    public const string Committed = "committed";
    public const string Failed = "failed";
}

public sealed record ModulePublicationAttempt
{
    public required Guid Id { get; init; }
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Version { get; init; }
    public required string State { get; init; }
    public required string StagingKey { get; init; }
    public string? ExpectedRevision { get; init; }
    public string? CommittedRevision { get; init; }
    public string? Error { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
