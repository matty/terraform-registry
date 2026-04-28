namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleExtractionService
{
    void Queue(ModuleExtractionRequest request);
}
