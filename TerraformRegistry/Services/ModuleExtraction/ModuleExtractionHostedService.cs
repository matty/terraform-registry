using Microsoft.Extensions.Options;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionHostedService : BackgroundService
{
    private readonly IModuleExtractionConfigService _configService;
    private readonly IModuleExtractionService _extractionService;
    private readonly ILogger<ModuleExtractionHostedService> _logger;
    private readonly ModuleExtractionOptions _options;

    public ModuleExtractionHostedService(
        IModuleExtractionService extractionService,
        IModuleExtractionConfigService configService,
        IOptions<ModuleExtractionOptions> options,
        ILogger<ModuleExtractionHostedService> logger)
    {
        _extractionService = extractionService;
        _configService = configService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await QueueBackfillAsync(stoppingToken);

        var workers = Enumerable.Range(0, _options.WorkerConcurrency)
            .Select(index => RunWorkerAsync($"{Environment.MachineName}-{Environment.ProcessId}-{index}", stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task QueueBackfillAsync(CancellationToken stoppingToken)
    {
        if (_options.StartupBackfillBatchSize <= 0)
            return;

        try
        {
            var queued = await _extractionService.QueueBackfillAsync(_options.StartupBackfillBatchSize, stoppingToken);
            if (queued.Count > 0)
            {
                RegistryLog.Information(_logger, "Queued {Count} modules for startup extraction backfill.", queued.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "Failed to queue module extraction startup backfill.");
        }
    }

    private async Task WaitUntilEnabledAsync(CancellationToken stoppingToken)
    {
        while (!await _configService.IsEnabledAsync(stoppingToken))
        {
            RegistryLog.Information(_logger, "Module extraction is disabled. Waiting before processing queued work.");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task RunWorkerAsync(string ownerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WaitUntilEnabledAsync(stoppingToken);
                if (!await _extractionService.ProcessNextAsync(ownerId, stoppingToken))
                    await Task.Delay(_options.JobPollIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RegistryLog.Error(_logger, ex, "Durable module extraction worker {OwnerId} failed.", ownerId);
                await Task.Delay(_options.JobPollIntervalMilliseconds, stoppingToken);
            }
        }
    }
}
