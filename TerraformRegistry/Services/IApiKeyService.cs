using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public interface IApiKeyService
{
    /// <summary>
    /// Creates a new API key for the specified user.
    /// Returns the raw token string (only shown once) and the created ApiKey entity.
    /// </summary>
    Task<(string RawToken, ApiKey Key)> CreateApiKeyAsync(string userId, string description, bool isShared = false);

    /// <summary>
    /// Validates a raw token and returns the key details if valid.
    /// Also updates the LastUsedAt timestamp.
    /// </summary>
    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken);

    /// <summary>
    /// Gets a single API key by identifier.
    /// </summary>
    Task<ApiKey?> GetApiKeyAsync(Guid id);

    /// <summary>
    /// Lists all API keys for a user.
    /// </summary>
    Task<IEnumerable<ApiKey>> ListApiKeysAsync(string userId);

    /// <summary>
    /// Lists all globally shared API keys.
    /// </summary>
    Task<IEnumerable<ApiKey>> ListSharedApiKeysAsync();

    /// <summary>
    /// Revokes (deletes) an API key.
    /// Users can revoke their own keys. Admins/System can revoke any.
    /// </summary>
    Task<bool> RevokeApiKeyAsync(Guid keyId, string userId);

    /// <summary>
    /// Updates an API key's metadata (description/shared) enforcing owner-only permission.
    /// </summary>
    Task<ApiKeyUpdateResult> UpdateApiKeyAsync(Guid keyId, string requestingUserId, string description, bool isShared);

    /// <summary>
    /// Ensures an OIDC login only binds to a matching provider identity.
    /// </summary>
    Task<User> GetOrCreateOidcUserAsync(string email, string provider, string providerId);

    /// <summary>
    /// Ensures a User record exists for the given external details.
    /// </summary>
    Task<User> GetOrCreateUserAsync(string email, string provider, string providerId);

    /// <summary>
    /// Retrieves a user by id.
    /// </summary>
    Task<User?> GetUserByIdAsync(string id);
}
