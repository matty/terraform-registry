using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IOutboxDeliveryHandler
{
    bool CanHandle(string kind);
    Task HandleAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}
