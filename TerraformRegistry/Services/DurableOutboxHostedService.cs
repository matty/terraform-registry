using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

public sealed class DurableOutboxHostedService(
    IOutboxEventRepository repository,
    IEnumerable<IOutboxDeliveryHandler> handlers,
    IOptions<DurableOutboxOptions> options,
    ILogger<DurableOutboxHostedService> logger) : BackgroundService
{
    private readonly DurableOutboxOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, options.WorkerConcurrency)
            .Select(index => RunWorkerAsync($"{Environment.MachineName}-{Environment.ProcessId}-outbox-{index}", stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(string ownerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TerraformRegistry.Models.OutboxEvent? @event = null;
            try
            {
                @event = await repository.TryClaimNextAsync(ownerId, TimeSpan.FromSeconds(options.LeaseSeconds), stoppingToken);
                if (@event is null) { await Task.Delay(options.PollIntervalMilliseconds, stoppingToken); continue; }
                var handler = handlers.FirstOrDefault(candidate => candidate.CanHandle(@event.Kind));
                if (handler is null) throw new InvalidOperationException($"No durable outbox handler is registered for '{@event.Kind}'.");
                await handler.HandleAsync(@event, stoppingToken);
                await repository.TryCompleteAsync(@event.Id, ownerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                RegistryLog.Error(logger, ex, "Durable outbox worker {OwnerId} failed to deliver an event.", ownerId);
                if (@event is not null)
                    await repository.TryFailAsync(@event.Id, ownerId, ex.Message, options.RetryLimit, stoppingToken);
                await Task.Delay(options.PollIntervalMilliseconds, stoppingToken);
            }
        }
    }
}
