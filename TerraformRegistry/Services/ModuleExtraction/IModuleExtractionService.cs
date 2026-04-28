namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleExtractionService
{
    void Queue(ModuleExtractionRequest request);
    IAsyncEnumerable<ModuleExtractionRequest> ReadQueuedAsync(CancellationToken cancellationToken);
    Task ExtractAsync(ModuleExtractionRequest request, CancellationToken cancellationToken);
}
