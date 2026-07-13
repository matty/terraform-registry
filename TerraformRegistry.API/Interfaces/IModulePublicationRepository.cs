using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IModulePublicationRepository
{
    Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job);
    Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id);
    Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id);
}
