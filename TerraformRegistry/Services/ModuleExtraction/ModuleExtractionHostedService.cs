using Microsoft.Extensions.Options;

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

        await foreach (var request in _extractionService.ReadQueuedAsync(stoppingToken))
        {
            try
            {
                await WaitUntilEnabledAsync(stoppingToken);
                await _extractionService.ExtractAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Module extraction failed for {Namespace}/{Name}/{Provider}/{Version}",
                    request.Namespace,
                    request.Name,
                    request.Provider,
                    request.Version);
            }
        }
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
                _logger.LogInformation("Queued {Count} modules for startup extraction backfill.", queued.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue module extraction startup backfill.");
        }
    }

    private async Task WaitUntilEnabledAsync(CancellationToken stoppingToken)
    {
        while (!await _configService.IsEnabledAsync(stoppingToken))
        {
            _logger.LogInformation("Module extraction is disabled. Waiting before processing queued work.");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
