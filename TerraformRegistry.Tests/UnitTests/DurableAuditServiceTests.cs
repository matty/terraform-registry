using System.Text.Json;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class DurableAuditServiceTests
{
    [Fact]
    public async Task LogAsyncEnqueuesAnAuditEventInsteadOfWritingItInline()
    {
        var store = new Mock<IAuditLogStore>();
        var outbox = new Mock<IOutboxEventRepository>();
        OutboxEvent? enqueued = null;
        outbox.Setup(repository => repository.EnqueueAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxEvent, CancellationToken>((@event, _) => enqueued = @event)
            .ReturnsAsync(true);
        var service = new DurableAuditService(store.Object, outbox.Object);

        await service.LogAsync("user-1", "module.deleted", "module", "example/vpc/aws/1.0.0", new { reason = "test" }, "127.0.0.1");

        store.Verify(store => store.LogAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<object?>(), It.IsAny<string?>()), Times.Never);
        Assert.NotNull(enqueued);
        Assert.Equal(AuditOutboxDeliveryHandler.Kind, enqueued.Kind);
        var payload = JsonSerializer.Deserialize<AuditOutboxPayload>(enqueued.PayloadJson);
        Assert.Equal("module.deleted", payload?.Action);
    }
}
