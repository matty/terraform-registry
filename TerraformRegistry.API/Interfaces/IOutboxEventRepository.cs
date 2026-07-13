using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IOutboxEventRepository
{
    Task<bool> EnqueueAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken = default);
    Task<OutboxEvent?> TryClaimNextAsync(string ownerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> TryCompleteAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);
    Task<bool> TryFailAsync(Guid id, string ownerId, string failureReason, int maximumAttempts, CancellationToken cancellationToken = default);
}
