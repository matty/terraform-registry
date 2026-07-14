using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionService : IModuleExtractionService
{
    private readonly IDatabaseService _databaseService;
    private readonly IModuleExtractionConfigService _configService;
    private readonly ITerraformModuleInspector _inspector;
    private readonly IModuleLlmContextGenerator _llmContextGenerator;
    private readonly ILogger<ModuleExtractionService> _logger;
    private readonly IModuleService _moduleService;
    private readonly IArchiveWorkspaceFactory _workspaceFactory;
    private readonly ModuleExtractionOptions _options;
    private readonly OperationalMetrics? _metrics;

    public ModuleExtractionService(
        IModuleService moduleService,
        IDatabaseService databaseService,
        IArchiveWorkspaceFactory workspaceFactory,
        ITerraformModuleInspector inspector,
        IModuleLlmContextGenerator llmContextGenerator,
        IModuleExtractionConfigService configService,
        ILogger<ModuleExtractionService> logger,
        ModuleExtractionOptions? options = null,
        OperationalMetrics? metrics = null)
    {
        _moduleService = moduleService;
        _databaseService = databaseService;
        _workspaceFactory = workspaceFactory;
        _inspector = inspector;
        _llmContextGenerator = llmContextGenerator;
        _configService = configService;
        _logger = logger;
        _options = options ?? new ModuleExtractionOptions();
        _metrics = metrics;
    }

    public async Task<bool> QueueAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        if (!await _configService.IsEnabledAsync(cancellationToken))
            return false;

        var pendingJobs = await _databaseService.CountPendingExtractionJobsAsync(cancellationToken);
        _metrics?.RecordExtractionQueueDepth(pendingJobs);
        if (pendingJobs >= _options.MaxPendingJobs)
        {
            RegistryLog.Warning(_logger,
                "Extraction backlog is full; rejecting module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
            return false;
        }

        var now = DateTime.UtcNow;
        var attempt = new ModulePublicationAttempt
        {
            Id = Guid.NewGuid(),
            Namespace = request.Namespace,
            Name = request.Name,
            Provider = request.Provider,
            Version = request.Version,
            State = ModulePublicationAttemptState.Committed,
            StagingKey = $"extraction-jobs/{Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = now
        };
        var job = new ModuleExtractionJob
        {
            Id = Guid.NewGuid(),
            PublicationAttemptId = attempt.Id,
            Namespace = request.Namespace,
            Name = request.Name,
            Provider = request.Provider,
            Version = request.Version,
            State = ModuleExtractionJobState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _databaseService.CreatePublicationAttemptWithExtractionJobAsync(attempt, job, cancellationToken);
        _metrics?.RecordExtractionQueueDepth(pendingJobs + 1);
        await MarkPendingAsync(request);
        return true;
    }

    public async Task<IReadOnlyList<ModuleExtractionRequest>> QueueBackfillAsync(int limit,
        CancellationToken cancellationToken)
    {
        if (!await _configService.IsEnabledAsync(cancellationToken))
            return [];

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var modules = await _databaseService.ListModulesForExtractionBackfillAsync(boundedLimit);
        var queued = new List<ModuleExtractionRequest>();

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ModuleExtractionRequest(
                module.Namespace,
                module.Name,
                module.Provider,
                module.Version);

            if (await QueueAsync(request, cancellationToken))
                queued.Add(request);
        }

        return queued;
    }

    public async Task<bool> ProcessNextAsync(string ownerId, CancellationToken cancellationToken)
    {
        var leaseDuration = TimeSpan.FromSeconds(_options.JobLeaseSeconds);
        var job = await _databaseService.TryClaimNextExtractionJobAsync(ownerId, leaseDuration, cancellationToken);
        if (job is null)
            return false;

        _metrics?.RecordExtractionClaim(job.CreatedAt);
        _metrics?.RecordExtractionAttempt();

        var request = new ModuleExtractionRequest(job.Namespace, job.Name, job.Provider, job.Version);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = MaintainLeaseAsync(job.Id, ownerId, leaseDuration, linkedCancellation);
        try
        {
            await ExtractAsync(request, linkedCancellation.Token);
            if (!await _databaseService.TryCompleteExtractionJobAsync(job.Id, ownerId, cancellationToken))
                RegistryLog.Warning(_logger, "Extraction lease was lost before completion for job {JobId}", job.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics?.RecordExtractionFailure("processing_failed");
            await _databaseService.TryFailExtractionJobAsync(job.Id, ownerId, Truncate(ex.Message, 2048),
                _options.JobRetryLimit, cancellationToken);
            RegistryLog.Error(_logger, ex, "Module extraction job {JobId} failed", job.Id);
        }
        finally
        {
            await linkedCancellation.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    private async Task MaintainLeaseAsync(Guid jobId, string ownerId, TimeSpan leaseDuration,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromTicks(Math.Max(leaseDuration.Ticks / 2, TimeSpan.FromSeconds(1).Ticks)),
                    cancellation.Token);
                if (!await _databaseService.TryHeartbeatExtractionJobAsync(jobId, ownerId, leaseDuration,
                        cancellation.Token))
                {
                    cancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            RegistryLog.Debug(_logger, "Extraction job lease maintenance stopped because the worker is shutting down.");
        }
    }

    public async Task ExtractAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await MarkProcessingAsync(request);

            await using var packageStream = await _moduleService.OpenModulePackageStreamAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);

            if (packageStream == null)
                throw new InvalidOperationException("Stored module package was not found.");

            await using var workspace = await _workspaceFactory.CreateAsync(packageStream, cancellationToken);
            var document = await _inspector.InspectAsync(workspace.RootPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var module = await _databaseService.GetModuleAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);

            if (module == null)
                throw new InvalidOperationException("Module metadata was not found.");

            var llmContext = _llmContextGenerator.Generate(module, document);

            await _databaseService.UpsertModuleExtractionAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                document);

            await _databaseService.UpsertModuleLlmContextAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                llmContext);

            await MarkSucceededAsync(request, document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryMarkFailedAsync(request, ex);
            throw;
        }
    }

    public async Task RegenerateLlmContextAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        try
        {
            await _databaseService.UpdateModuleMetadataAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                metadata =>
                {
                    metadata.LlmContext ??= new ModuleLlmContextState();
                    metadata.LlmContext.Status = "processing";
                    metadata.LlmContext.LastAttemptedAt = now;
                    metadata.LlmContext.LastUpdatedAt = now;
                    metadata.LlmContext.Error = null;
                });

            var module = await _databaseService.GetModuleAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
            if (module == null)
                throw new InvalidOperationException("Module metadata was not found.");

            var extraction = await _databaseService.GetModuleExtractionAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
            if (extraction == null)
                throw new InvalidOperationException("Module extraction document was not found.");

            var llmContext = _llmContextGenerator.Generate(module, extraction);

            await _databaseService.UpsertModuleLlmContextAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                llmContext);

            await _databaseService.UpdateModuleMetadataAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                metadata =>
                {
                    metadata.LlmContext ??= new ModuleLlmContextState();
                    metadata.LlmContext.Status = "succeeded";
                    metadata.LlmContext.LastAttemptedAt ??= now;
                    metadata.LlmContext.LastSucceededAt = now;
                    metadata.LlmContext.LastUpdatedAt = now;
                    metadata.LlmContext.Error = null;
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryMarkLlmFailedAsync(request, ex);
            throw;
        }
    }

    private Task MarkProcessingAsync(ModuleExtractionRequest request)
    {
        var now = DateTime.UtcNow;
        return _databaseService.UpdateModuleMetadataAsync(
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            metadata =>
            {
                metadata.Extraction ??= new ModuleExtractionState();
                metadata.Extraction.Status = "processing";
                metadata.Extraction.LastAttemptedAt = now;
                metadata.Extraction.LastUpdatedAt = now;
                metadata.Extraction.Error = null;
                metadata.LlmContext ??= new ModuleLlmContextState();
                metadata.LlmContext.Status = "processing";
                metadata.LlmContext.LastAttemptedAt = now;
                metadata.LlmContext.LastUpdatedAt = now;
                metadata.LlmContext.Error = null;
            });
    }

    private Task MarkPendingAsync(ModuleExtractionRequest request)
    {
        var now = DateTime.UtcNow;
        return _databaseService.UpdateModuleMetadataAsync(
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            metadata =>
            {
                metadata.Extraction ??= new ModuleExtractionState();
                metadata.Extraction.Status = "pending";
                metadata.Extraction.LastUpdatedAt = now;
                metadata.LlmContext ??= new ModuleLlmContextState();
                metadata.LlmContext.Status = "pending";
                metadata.LlmContext.LastUpdatedAt = now;
            });
    }

    private Task MarkSucceededAsync(ModuleExtractionRequest request, ModuleExtractionDocument document)
    {
        var now = DateTime.UtcNow;
        return _databaseService.UpdateModuleMetadataAsync(
            request.Namespace,
            request.Name,
            request.Provider,
            request.Version,
            metadata =>
            {
                metadata.ProviderRequirements = document.ProviderRequirements;
                metadata.Submodules = document.Submodules;
                metadata.Documentation = CreateDocumentationSummary(document);
                metadata.Extraction ??= new ModuleExtractionState();
                metadata.Extraction.Status = "succeeded";
                metadata.Extraction.LastAttemptedAt ??= now;
                metadata.Extraction.LastSucceededAt = now;
                metadata.Extraction.LastUpdatedAt = now;
                metadata.Extraction.Error = null;
                metadata.LlmContext ??= new ModuleLlmContextState();
                metadata.LlmContext.Status = "succeeded";
                metadata.LlmContext.LastAttemptedAt ??= now;
                metadata.LlmContext.LastSucceededAt = now;
                metadata.LlmContext.LastUpdatedAt = now;
                metadata.LlmContext.Error = null;
            });
    }

    private async Task TryMarkFailedAsync(ModuleExtractionRequest request, Exception exception)
    {
        var now = DateTime.UtcNow;

        try
        {
            await _databaseService.UpdateModuleMetadataAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                metadata =>
                {
                    metadata.Extraction ??= new ModuleExtractionState();
                    metadata.Extraction.Status = "failed";
                    metadata.Extraction.LastUpdatedAt = now;
                    metadata.Extraction.Error = Truncate(exception.Message, 2048);
                    metadata.LlmContext ??= new ModuleLlmContextState();
                    metadata.LlmContext.Status = "failed";
                    metadata.LlmContext.LastUpdatedAt = now;
                    metadata.LlmContext.Error = Truncate(exception.Message, 2048);
                });
        }
        catch (Exception metadataException)
        {
            RegistryLog.Error(_logger,
                metadataException,
                "Failed to mark extraction failure for module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
        }
    }

    private async Task TryMarkLlmFailedAsync(ModuleExtractionRequest request, Exception exception)
    {
        var now = DateTime.UtcNow;

        try
        {
            await _databaseService.UpdateModuleMetadataAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                metadata =>
                {
                    metadata.LlmContext ??= new ModuleLlmContextState();
                    metadata.LlmContext.Status = "failed";
                    metadata.LlmContext.LastUpdatedAt = now;
                    metadata.LlmContext.Error = Truncate(exception.Message, 2048);
                });
        }
        catch (Exception metadataException)
        {
            RegistryLog.Error(_logger,
                metadataException,
                "Failed to mark LLM context failure for module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
        }
    }

    private static ModuleDocumentationSummary CreateDocumentationSummary(ModuleExtractionDocument document)
    {
        return new ModuleDocumentationSummary
        {
            PrimaryReadmePath = document.Readme?.Path,
            InputCount = document.Inputs.Count,
            OutputCount = document.Outputs.Count,
            ExampleCount = document.Examples.Count,
            HasSubmoduleDocs = document.Submodules.Count > 0
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
