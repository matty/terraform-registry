using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class OutboxEventStateTests
{
    [Fact]
    public void PendingAndRetryEventsAreClaimable()
    {
        Assert.True(OutboxEventState.IsClaimable(OutboxEventState.Pending));
        Assert.True(OutboxEventState.IsClaimable(OutboxEventState.Retry));
        Assert.False(OutboxEventState.IsClaimable(OutboxEventState.Processing));
        Assert.False(OutboxEventState.IsClaimable(OutboxEventState.Delivered));
        Assert.False(OutboxEventState.IsClaimable(OutboxEventState.DeadLetter));
    }
}
