using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

public sealed class ModuleDownloadAnalyticsHostedService(
    ModuleDownloadAnalyticsBuffer queue,
    IDatabaseService databaseService,
    ILogger<ModuleDownloadAnalyticsHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var record in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await databaseService.RecordDownloadAsync(
                    record.Namespace,
                    record.Name,
                    record.Provider,
                    record.Version,
                    record.ClientIp,
                    record.UserAgent);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                RegistryLog.Error(logger, ex,
                    "Dropped download analytics record for {Namespace}/{Name}/{Provider}/{Version} after persistence failed.",
                    record.Namespace, record.Name, record.Provider, record.Version);
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        return base.StopAsync(cancellationToken);
    }
}
