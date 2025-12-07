using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

/// <summary>
///     Hosted service to initialize the database at application startup.
///     Includes configurable retry logic with exponential backoff for handling
///     transient connection failures (e.g., when database is still starting up).
/// </summary>
public class DatabaseInitializerHostedService : IHostedService
{
    private readonly IInitializableDb? _initializableDb;
    private readonly ILogger<DatabaseInitializerHostedService> _logger;
    private readonly DatabaseRetryOptions _retryOptions;

    public DatabaseInitializerHostedService(
        IServiceProvider serviceProvider,
        IOptions<DatabaseRetryOptions> retryOptions,
        ILogger<DatabaseInitializerHostedService> logger)
    {
        _initializableDb = serviceProvider.GetService(typeof(IInitializableDb)) as IInitializableDb;
        _retryOptions = retryOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_initializableDb == null)
        {
            _logger.LogWarning("No IInitializableDb service found. Skipping database initialization.");
            return;
        }

        var pipeline = CreateRetryPipeline();
        
        await pipeline.ExecuteAsync(async token =>
        {
            _logger.LogInformation("Attempting to initialize database...");
            await _initializableDb.InitializeDatabase();
            _logger.LogInformation("Database initialization completed successfully.");
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private ResiliencePipeline CreateRetryPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _retryOptions.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(_retryOptions.InitialDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(_retryOptions.MaxDelaySeconds),
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Database connection attempt {AttemptNumber} of {MaxAttempts} failed. Retrying in {Delay}...",
                        args.AttemptNumber + 1,
                        _retryOptions.MaxRetryAttempts,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}