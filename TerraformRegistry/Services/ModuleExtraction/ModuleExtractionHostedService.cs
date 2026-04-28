using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionHostedService : BackgroundService
{
    private readonly IDatabaseService _databaseService;
    private readonly IModuleExtractionService _extractionService;
    private readonly ILogger<ModuleExtractionHostedService> _logger;
    private readonly ModuleExtractionOptions _options;

    public ModuleExtractionHostedService(
        IModuleExtractionService extractionService,
        IDatabaseService databaseService,
        IOptions<ModuleExtractionOptions> options,
        ILogger<ModuleExtractionHostedService> logger)
    {
        _extractionService = extractionService;
        _databaseService = databaseService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Module extraction is disabled.");
            return;
        }

        await QueueBackfillAsync(stoppingToken);

        await foreach (var request in _extractionService.ReadQueuedAsync(stoppingToken))
        {
            try
            {
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
            var modules = await _databaseService.ListModulesNeedingExtractionAsync(_options.StartupBackfillBatchSize);
            foreach (var module in modules)
            {
                stoppingToken.ThrowIfCancellationRequested();
                _extractionService.Queue(new ModuleExtractionRequest(
                    module.Namespace,
                    module.Name,
                    module.Provider,
                    module.Version));
            }

            if (modules.Count > 0)
            {
                _logger.LogInformation("Queued {Count} modules for startup extraction backfill.", modules.Count);
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
}
