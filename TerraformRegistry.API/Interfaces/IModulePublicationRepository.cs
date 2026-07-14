using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IModulePublicationRepository
{
    Task CreatePublicationAttemptWithExtractionJobAsync(ModulePublicationAttempt attempt, ModuleExtractionJob job,
        CancellationToken cancellationToken = default);
    Task<bool> TryCommitStagedPublicationAsync(
        ModulePublicationAttempt attempt,
        ModuleStorage newModule,
        ModuleStorage? expectedModule, CancellationToken cancellationToken = default);
    Task<bool> TryFailStagedPublicationAsync(Guid attemptId, string failureReason,
        CancellationToken cancellationToken = default);
    Task<ModulePublicationAttempt?> GetPublicationAttemptAsync(Guid id);
    Task<ModuleExtractionJob?> GetExtractionJobAsync(Guid id);
}
