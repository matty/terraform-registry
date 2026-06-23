using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IMirrorLeaseRepository
{
    Task<MirrorCacheLease?> GetLeaseAsync(
        string leaseKey,
        CancellationToken cancellationToken = default);

    Task UpsertLeaseAsync(
        MirrorCacheLease lease,
        CancellationToken cancellationToken = default);

    Task<MirrorCacheLease?> TryAcquireAsync(
        string leaseKey,
        string operationType,
        string ownerInstanceId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(
        string leaseKey,
        string ownerInstanceId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        string leaseKey,
        string ownerInstanceId,
        CancellationToken cancellationToken = default);
}
