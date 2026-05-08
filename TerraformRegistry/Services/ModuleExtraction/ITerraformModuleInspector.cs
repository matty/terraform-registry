using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public interface ITerraformModuleInspector
{
    Task<ModuleExtractionDocument> InspectAsync(string modulePath, CancellationToken cancellationToken);
}
