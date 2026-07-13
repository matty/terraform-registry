using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

public sealed class DurableOutboxHostedService(
    DurableOutboxProcessor processor,
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
            try
            {
                if (await processor.ProcessNextAsync(ownerId, stoppingToken)) continue;
                await Task.Delay(options.PollIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                RegistryLog.Error(logger, ex, "Durable outbox worker {OwnerId} failed to deliver an event.", ownerId);
                await Task.Delay(options.PollIntervalMilliseconds, stoppingToken);
            }
        }
    }
}
