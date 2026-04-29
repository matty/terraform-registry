using System.Threading.Channels;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionService : IModuleExtractionService
{
    private readonly Channel<ModuleExtractionRequest> _queue =
        Channel.CreateUnbounded<ModuleExtractionRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly IDatabaseService _databaseService;
    private readonly IModuleExtractionConfigService _configService;
    private readonly ITerraformModuleInspector _inspector;
    private readonly IModuleLlmContextGenerator _llmContextGenerator;
    private readonly ILogger<ModuleExtractionService> _logger;
    private readonly IModuleService _moduleService;
    private readonly IArchiveWorkspaceFactory _workspaceFactory;

    public ModuleExtractionService(
        IModuleService moduleService,
        IDatabaseService databaseService,
        IArchiveWorkspaceFactory workspaceFactory,
        ITerraformModuleInspector inspector,
        IModuleLlmContextGenerator llmContextGenerator,
        IModuleExtractionConfigService configService,
        ILogger<ModuleExtractionService> logger)
    {
        _moduleService = moduleService;
        _databaseService = databaseService;
        _workspaceFactory = workspaceFactory;
        _inspector = inspector;
        _llmContextGenerator = llmContextGenerator;
        _configService = configService;
        _logger = logger;
    }

    public async Task<bool> QueueAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        if (!await _configService.IsEnabledAsync(cancellationToken))
            return false;

        if (!_queue.Writer.TryWrite(request))
        {
            _logger.LogWarning(
                "Unable to queue extraction for module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
            return false;
        }

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

            if (_queue.Writer.TryWrite(request))
            {
                await MarkPendingAsync(request);
                queued.Add(request);
            }
        }

        return queued;
    }

    public IAsyncEnumerable<ModuleExtractionRequest> ReadQueuedAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
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
            _logger.LogError(
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
            _logger.LogError(
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
