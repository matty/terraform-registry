namespace TerraformRegistry.Models;

public static class OutboxEventState
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retry = "retry";
    public const string Delivered = "delivered";
    public const string DeadLetter = "dead-letter";

    public static bool IsClaimable(string state) => state is Pending or Retry;
}

public sealed record OutboxEvent
{
    public required Guid Id { get; init; }
    public required string Kind { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string PayloadJson { get; init; }
    public required string State { get; init; }
    public string? OwnerId { get; init; }
    public DateTime? LeaseExpiresAt { get; init; }
    public int AttemptCount { get; init; }
    public string? LastError { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
}
