using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services
{
    /// <summary>
    /// Hosted service to initialize the database at application startup.
    /// </summary>
    public class DatabaseInitializerHostedService : IHostedService
    {
        private readonly IInitializableDb? _initializableDb;

        public DatabaseInitializerHostedService(IServiceProvider serviceProvider)
        {
            // Try to resolve IInitializableDb (may be null for in-memory DB)
            _initializableDb = serviceProvider.GetService(typeof(IInitializableDb)) as IInitializableDb;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_initializableDb != null)
            {
                await _initializableDb.InitializeDatabase();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
