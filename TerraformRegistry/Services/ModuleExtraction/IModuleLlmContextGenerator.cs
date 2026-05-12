using TerraformRegistry.Models;

namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleLlmContextGenerator
{
    ModuleLlmContextDocument Generate(TerraformModule terraformModule, ModuleExtractionDocument extraction);
}
