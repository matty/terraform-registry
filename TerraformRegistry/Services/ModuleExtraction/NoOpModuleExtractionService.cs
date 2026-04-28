using System.Runtime.CompilerServices;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class NoOpModuleExtractionService : IModuleExtractionService
{
    public void Queue(ModuleExtractionRequest request)
    {
    }

    public async IAsyncEnumerable<ModuleExtractionRequest> ReadQueuedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task ExtractAsync(ModuleExtractionRequest request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
