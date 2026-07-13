using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

/// <summary>
///     Runs storage reconciliation after readiness without delaying application startup.
/// </summary>
public sealed class StorageReconciliationHostedService(
    IModuleService moduleService,
    ILogger<StorageReconciliationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await moduleService.ReconcileStorageAsync(stoppingToken);
                RegistryLog.Information(logger, "Storage reconciliation completed successfully.");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                RegistryLog.Error(logger, ex, "Storage reconciliation failed; retrying after {RetryDelay}.", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
