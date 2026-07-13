namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Compatibility interface for database services.
/// </summary>
public interface IDatabaseService :
    IModuleRepository,
    IModuleExtractionRepository,
    IUserRepository,
    IApiKeyRepository,
    IModuleDownloadRecorder,
    IModulePublicationRepository
{
    /// <summary>
    ///     Checks that the database connection is healthy.
    /// </summary>
    Task<bool> CheckConnectionAsync();
}
