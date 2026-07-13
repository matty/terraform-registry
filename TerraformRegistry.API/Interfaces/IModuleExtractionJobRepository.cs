using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
/// Provides durable, lease-based coordination for module extraction work.
/// </summary>
public interface IModuleExtractionJobRepository
{
    Task<ModuleExtractionJob?> TryClaimNextExtractionJobAsync(
        string ownerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> TryHeartbeatExtractionJobAsync(
        Guid jobId,
        string ownerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteExtractionJobAsync(
        Guid jobId,
        string ownerId,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailExtractionJobAsync(
        Guid jobId,
        string ownerId,
        string failureReason,
        int maximumAttempts,
        CancellationToken cancellationToken = default);

    Task<int> CountPendingExtractionJobsAsync(CancellationToken cancellationToken = default);
}
