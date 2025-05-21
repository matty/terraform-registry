using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

/// <summary>
///     Hosted service to initialize the database at application startup.
/// </summary>
public class DatabaseInitializerHostedService(IServiceProvider serviceProvider) : IHostedService
{
    private readonly IInitializableDb? _initializableDb = serviceProvider.GetService(typeof(IInitializableDb)) as IInitializableDb;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_initializableDb != null) await _initializableDb.InitializeDatabase();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}