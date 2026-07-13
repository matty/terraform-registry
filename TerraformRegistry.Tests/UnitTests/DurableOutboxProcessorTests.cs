using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class DurableOutboxProcessorTests
{
    [Fact]
    public async Task ProcessNextAsyncCompletesAHandledEvent()
    {
        var @event = CreateEvent();
        var repository = new Mock<IOutboxEventRepository>();
        repository.Setup(x => x.TryClaimNextAsync("worker-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(@event);
        repository.Setup(x => x.TryCompleteAsync(@event.Id, "worker-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new Mock<IOutboxDeliveryHandler>();
        handler.Setup(x => x.CanHandle("test")).Returns(true);
        handler.Setup(x => x.HandleAsync(@event, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var processor = CreateProcessor(repository.Object, handler.Object);

        Assert.True(await processor.ProcessNextAsync("worker-1", CancellationToken.None));
        repository.Verify(x => x.TryCompleteAsync(@event.Id, "worker-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessNextAsyncSchedulesRetryWhenDeliveryFails()
    {
        var @event = CreateEvent();
        var repository = new Mock<IOutboxEventRepository>();
        repository.Setup(x => x.TryClaimNextAsync("worker-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(@event);
        repository.Setup(x => x.TryFailAsync(@event.Id, "worker-1", "failed", 5, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new Mock<IOutboxDeliveryHandler>();
        handler.Setup(x => x.CanHandle("test")).Returns(true);
        handler.Setup(x => x.HandleAsync(@event, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("failed"));
        var processor = CreateProcessor(repository.Object, handler.Object);

        Assert.False(await processor.ProcessNextAsync("worker-1", CancellationToken.None));
        repository.Verify(x => x.TryFailAsync(@event.Id, "worker-1", "failed", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DurableOutboxProcessor CreateProcessor(IOutboxEventRepository repository, IOutboxDeliveryHandler handler) =>
        new(repository, [handler], Options.Create(new DurableOutboxOptions { LeaseSeconds = 30, RetryLimit = 5 }),
            NullLogger<DurableOutboxProcessor>.Instance);

    private static OutboxEvent CreateEvent() => new()
    {
        Id = Guid.NewGuid(),
        Kind = "test",
        IdempotencyKey = "test:event",
        PayloadJson = "{}",
        State = OutboxEventState.Processing,
        OwnerId = "worker-1",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
