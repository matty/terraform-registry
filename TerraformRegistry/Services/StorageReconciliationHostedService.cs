using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

/// <summary>
///     Runs storage reconciliation after readiness without delaying application startup.
/// </summary>
public sealed class StorageReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly IModuleService _moduleService;
    private readonly ILogger<StorageReconciliationHostedService> _logger;

    public StorageReconciliationHostedService(
        IModuleService moduleService,
        ILogger<StorageReconciliationHostedService> logger)
    {
        _moduleService = moduleService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _moduleService.ReconcileStorageAsync(stoppingToken);
                RegistryLog.Information(_logger, "Storage reconciliation completed successfully.");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RegistryLog.Error(_logger, ex, "Storage reconciliation failed; retrying after {RetryDelay}.", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
