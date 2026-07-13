using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IModulePublicationRepository
{
    Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job);
    Task<bool> TryCommitStagedPublicationAsync(
        ModulePublicationAttempt attempt,
        ModuleStorage newModule,
        ModuleStorage? expectedModule);
    Task<bool> TryFailStagedPublicationAsync(Guid attemptId, string failureReason);
    Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id);
    Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id);
}
