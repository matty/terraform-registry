using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IMirrorLeaseService
{
    Task<MirrorLeaseHandle?> TryAcquireAsync(
        string leaseKey,
        string operationType,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(
        MirrorLeaseHandle handle,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        MirrorLeaseHandle handle,
        CancellationToken cancellationToken = default);
}
