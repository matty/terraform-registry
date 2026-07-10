using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services;

/// <summary>
///     Runs the initial storage pass after database migration. Registration order ensures the
///     database initializer completes before this service is started.
/// </summary>
public sealed class StorageInitializationHostedService(
    IServiceProvider serviceProvider,
    IStartupReadiness startupReadiness,
    ILogger<StorageInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var moduleService = serviceProvider.GetRequiredService<IModuleService>();
        await moduleService.InitializeStorageAsync(cancellationToken);
        startupReadiness.MarkStorageInitialized();
        RegistryLog.Information(logger, "Initial storage initialization completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
