namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleExtractionService
{
    Task<bool> QueueAsync(ModuleExtractionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModuleExtractionRequest>> QueueBackfillAsync(int limit, CancellationToken cancellationToken);
    Task<bool> ProcessNextAsync(string ownerId, CancellationToken cancellationToken);
    Task ExtractAsync(ModuleExtractionRequest request, CancellationToken cancellationToken);
    Task RegenerateLlmContextAsync(ModuleExtractionRequest request, CancellationToken cancellationToken);
}
