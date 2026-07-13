using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IMirrorLeaseRepository
{
    Task<IReadOnlyList<MirrorCacheLease>> ListLeasesAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

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
        Guid leaseId,
        string leaseKey,
        string ownerInstanceId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        Guid leaseId,
        string leaseKey,
        string ownerInstanceId,
        CancellationToken cancellationToken = default);
}
