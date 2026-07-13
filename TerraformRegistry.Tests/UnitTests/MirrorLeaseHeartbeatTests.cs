using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class MirrorLeaseHeartbeatTests
{
    [Fact]
    public async Task MarksOwnershipLostWhenHeartbeatIsRejected()
    {
        var leaseService = new Mock<IMirrorLeaseService>();
        leaseService.Setup(x => x.HeartbeatAsync(It.IsAny<MirrorLeaseHandle>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handle = new MirrorLeaseHandle
        {
            Id = Guid.NewGuid(),
            LeaseKey = "provider:hashicorp/aws",
            OperationType = "provider-package",
            OwnerInstanceId = "worker-a",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        await using var heartbeat = new MirrorLeaseHeartbeat(leaseService.Object, handle, TimeSpan.Zero);

        await heartbeat.WaitForFirstHeartbeatAsync();

        Assert.True(heartbeat.IsOwnershipLost);
    }
}
