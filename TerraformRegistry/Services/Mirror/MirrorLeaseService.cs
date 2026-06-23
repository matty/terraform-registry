using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorLeaseService(IMirrorLeaseRepository repository) : IMirrorLeaseService
{
    private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromMinutes(5);
    private readonly string _ownerInstanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task<MirrorLeaseHandle?> TryAcquireAsync(
        string leaseKey,
        string operationType,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var lease = await repository.TryAcquireAsync(
            leaseKey,
            operationType,
            _ownerInstanceId,
            ttl,
            cancellationToken);

        return lease is null ? null : ToHandle(lease);
    }

    public Task<bool> HeartbeatAsync(
        MirrorLeaseHandle handle,
        CancellationToken cancellationToken = default)
    {
        return repository.HeartbeatAsync(
            handle.Id,
            handle.LeaseKey,
            handle.OwnerInstanceId,
            HeartbeatTtl,
            cancellationToken);
    }

    public Task<bool> ReleaseAsync(
        MirrorLeaseHandle handle,
        CancellationToken cancellationToken = default)
    {
        return repository.ReleaseAsync(
            handle.Id,
            handle.LeaseKey,
            handle.OwnerInstanceId,
            cancellationToken);
    }

    private static MirrorLeaseHandle ToHandle(MirrorCacheLease lease) =>
        new()
        {
            Id = lease.Id,
            LeaseKey = lease.LeaseKey,
            OperationType = lease.OperationType,
            OwnerInstanceId = lease.OwnerInstanceId,
            ExpiresAt = lease.ExpiresAt
        };
}
