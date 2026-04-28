using System.Threading.Channels;
using Microsoft.Extensions.Options;
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
    private readonly ITerraformModuleInspector _inspector;
    private readonly ILogger<ModuleExtractionService> _logger;
    private readonly IModuleService _moduleService;
    private readonly ModuleExtractionOptions _options;
    private readonly IArchiveWorkspaceFactory _workspaceFactory;

    public ModuleExtractionService(
        IModuleService moduleService,
        IDatabaseService databaseService,
        IArchiveWorkspaceFactory workspaceFactory,
        ITerraformModuleInspector inspector,
        IOptions<ModuleExtractionOptions> options,
        ILogger<ModuleExtractionService> logger)
    {
        _moduleService = moduleService;
        _databaseService = databaseService;
        _workspaceFactory = workspaceFactory;
        _inspector = inspector;
        _options = options.Value;
        _logger = logger;
    }

    public void Queue(ModuleExtractionRequest request)
    {
        if (!_options.Enabled)
            return;

        if (!_queue.Writer.TryWrite(request))
        {
            _logger.LogWarning(
                "Unable to queue extraction for module {Namespace}/{Name}/{Provider}/{Version}",
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version);
        }
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

            await _databaseService.UpsertModuleExtractionAsync(
                request.Namespace,
                request.Name,
                request.Provider,
                request.Version,
                document);

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
