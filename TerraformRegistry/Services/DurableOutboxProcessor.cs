using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public sealed class DurableOutboxProcessor(
    IOutboxEventRepository repository,
    IEnumerable<IOutboxDeliveryHandler> handlers,
    IOptions<DurableOutboxOptions> options,
    ILogger<DurableOutboxProcessor> logger)
{
    private readonly DurableOutboxOptions options = options.Value;

    public async Task<bool> ProcessNextAsync(string ownerId, CancellationToken cancellationToken)
    {
        OutboxEvent? @event = null;
        try
        {
            @event = await repository.TryClaimNextAsync(ownerId, TimeSpan.FromSeconds(options.LeaseSeconds), cancellationToken);
            if (@event is null) return false;

            var handler = handlers.FirstOrDefault(candidate => candidate.CanHandle(@event.Kind));
            if (handler is null) throw new InvalidOperationException($"No durable outbox handler is registered for '{@event.Kind}'.");

            await handler.HandleAsync(@event, cancellationToken);
            if (!await repository.TryCompleteAsync(@event.Id, ownerId, cancellationToken))
                throw new InvalidOperationException("The durable outbox event lease was lost before it could be completed.");

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // lgtm[cs/catch-of-all-exceptions] Delivery handlers are extension points; every failure must be persisted for retry.
        catch (Exception ex)
        {
            RegistryLog.Error(logger, ex, "Durable outbox worker {OwnerId} failed to deliver an event.", ownerId);
            if (@event is not null)
            {
                try
                {
                    if (!await repository.TryFailAsync(@event.Id, ownerId, ex.Message, options.RetryLimit, cancellationToken))
                        RegistryLog.Warning(logger, "Durable outbox worker {OwnerId} lost the lease while recording a delivery failure.", ownerId);
                }
                catch (Exception retryEx) when (!cancellationToken.IsCancellationRequested)
                {
                    RegistryLog.Error(logger, retryEx, "Durable outbox worker {OwnerId} could not record a delivery failure.", ownerId);
                }
            }

            return false;
        }
    }
}
