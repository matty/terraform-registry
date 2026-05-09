using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Stores API key records.
/// </summary>
public interface IApiKeyRepository
{
    Task AddApiKeyAsync(ApiKey apiKey);

    Task<ApiKey?> GetApiKeyAsync(Guid id);

    Task<IEnumerable<ApiKey>> GetApiKeysByUserAsync(string userId);

    Task<IEnumerable<ApiKey>> GetSharedApiKeysAsync();

    Task<IEnumerable<ApiKey>> GetApiKeysByPrefixAsync(string prefix);

    Task UpdateApiKeyAsync(ApiKey apiKey);

    Task DeleteApiKeyAsync(ApiKey apiKey);
}
