namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class NoOpModuleExtractionService : IModuleExtractionService
{
    public void Queue(ModuleExtractionRequest request)
    {
    }
}
