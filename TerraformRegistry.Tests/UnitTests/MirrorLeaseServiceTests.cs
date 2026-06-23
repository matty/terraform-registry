using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Mirror;

namespace TerraformRegistry.Tests.UnitTests;

public class MirrorLeaseServiceTests
{
    [Fact]
    public async Task TryAcquireAsyncReturnsHandleUsingStableOwnerInstanceId()
    {
        var repository = new Mock<IMirrorLeaseRepository>();
        string? ownerInstanceId = null;
        repository.Setup(x => x.TryAcquireAsync(
                "provider/hashicorp/aws",
                "provider-sync",
                It.IsAny<string>(),
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, TimeSpan, CancellationToken>((_, _, owner, _, _) => ownerInstanceId = owner)
            .ReturnsAsync(() => new MirrorCacheLease
            {
                Id = Guid.NewGuid(),
                LeaseKey = "provider/hashicorp/aws",
                OperationType = "provider-sync",
                OwnerInstanceId = ownerInstanceId!,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
        var service = new MirrorLeaseService(repository.Object);

        var handle = await service.TryAcquireAsync(
            "provider/hashicorp/aws",
            "provider-sync",
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.NotNull(handle);
        Assert.Equal("provider/hashicorp/aws", handle.LeaseKey);
        Assert.False(string.IsNullOrWhiteSpace(handle.OwnerInstanceId));
        Assert.Equal(ownerInstanceId, handle.OwnerInstanceId);
    }

    [Fact]
    public async Task TryAcquireAsyncReturnsNullWhenRepositoryDoesNotAcquire()
    {
        var repository = new Mock<IMirrorLeaseRepository>();
        repository.Setup(x => x.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MirrorCacheLease?)null);
        var service = new MirrorLeaseService(repository.Object);

        var handle = await service.TryAcquireAsync("lease-key", "operation", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Null(handle);
    }

    [Fact]
    public async Task HeartbeatAndReleaseUseHandleOwner()
    {
        var repository = new Mock<IMirrorLeaseRepository>();
        var service = new MirrorLeaseService(repository.Object);
        var handle = new MirrorLeaseHandle
        {
            Id = Guid.NewGuid(),
            LeaseKey = "module/internal/network/aws",
            OperationType = "module-sync",
            OwnerInstanceId = "owner-1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        repository.Setup(x => x.HeartbeatAsync(
                handle.Id,
                handle.LeaseKey,
                handle.OwnerInstanceId,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(x => x.ReleaseAsync(
                handle.Id,
                handle.LeaseKey,
                handle.OwnerInstanceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await service.HeartbeatAsync(handle, CancellationToken.None));
        Assert.True(await service.ReleaseAsync(handle, CancellationToken.None));
    }
}
