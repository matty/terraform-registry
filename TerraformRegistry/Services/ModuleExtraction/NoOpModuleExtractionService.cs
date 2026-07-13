namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class NoOpModuleExtractionService : IModuleExtractionService
{
    public Task<bool> QueueAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<ModuleExtractionRequest>> QueueBackfillAsync(int limit, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ModuleExtractionRequest>>([]);
    }

    public Task<bool> ProcessNextAsync(string ownerId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task ExtractAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task RegenerateLlmContextAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
