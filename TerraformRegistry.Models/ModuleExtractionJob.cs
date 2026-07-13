namespace TerraformRegistry.Models;

public static class ModuleExtractionJobState
{
    public const string Staged = "staged";
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retry = "retry";
    public const string Succeeded = "succeeded";
    public const string DeadLetter = "dead-letter";
}

public sealed record ModuleExtractionJob
{
    public required Guid Id { get; init; }
    public required Guid PublicationAttemptId { get; init; }
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Version { get; init; }
    public required string State { get; init; }
    public string? OwnerId { get; init; }
    public DateTime? LeaseExpiresAt { get; init; }
    public int AttemptCount { get; init; }
    public string? LastError { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
