namespace TerraformRegistry.Services.ModuleExtraction;

public interface IModuleExtractionConfigService
{
    Task<ModuleExtractionRuntimeConfig> GetAsync(CancellationToken cancellationToken);
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
    Task<ModuleExtractionRuntimeConfig> SetEnabledAsync(bool enabled, string? updatedBy, CancellationToken cancellationToken);
}
