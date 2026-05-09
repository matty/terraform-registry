using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleLlmContextGenerator
{
    ModuleLlmContextDocument Generate(Module module, ModuleExtractionDocument extraction);
}
