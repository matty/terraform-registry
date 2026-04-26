using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class ApiKeyService(IDatabaseService dbService, ILogger<ApiKeyService> logger) : IApiKeyService
{
    private const int TokenLength = 32;
    // private const string TokenPrefix = "tf-"; // Removed prefix requirement

    public async Task<(string RawToken, ApiKey Key)> CreateApiKeyAsync(string userId, string description, bool isShared = false)
    {
        // Generate random token
        var randomBytes = RandomNumberGenerator.GetBytes(TokenLength);
        var tokenCore = Convert.ToBase64String(randomBytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", ""); // URL-safe base64
        var rawToken = tokenCore; // No prefix

        // Hash token using Argon2id
        var tokenHash = HashToken(rawToken);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Description = description,
            TokenHash = tokenHash,
            Prefix = rawToken.Substring(0, 8),
            IsShared = isShared,
            CreatedAt = DateTime.UtcNow
        };

        await dbService.AddApiKeyAsync(apiKey);

        logger.LogInformation("Created new API key {KeyId} for user {UserId}", apiKey.Id, userId);

        return (rawToken, apiKey);
    }

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new ApiKeyValidationResult(null, false);
        }

        var prefix = rawToken.Length >= 8 ? rawToken.Substring(0, 8) : rawToken;

        // Find keys with matching prefix to minimize Argon2 checks (expensive)
        var candidates = await dbService.GetApiKeysByPrefixAsync(prefix);

        foreach (var key in candidates)
        {
            if (VerifyHash(rawToken, key.TokenHash))
            {
                if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
                {
                    return new ApiKeyValidationResult(null, true);
                }

                key.LastUsedAt = DateTime.UtcNow;
                await dbService.UpdateApiKeyAsync(key);
                return new ApiKeyValidationResult(key, false);
            }
        }

        return new ApiKeyValidationResult(null, false);
    }

    public async Task<ApiKey?> GetApiKeyAsync(Guid id)
    {
        return await dbService.GetApiKeyAsync(id);
    }

    public async Task<IEnumerable<ApiKey>> ListApiKeysAsync(string userId)
    {
        return await dbService.GetApiKeysByUserAsync(userId);
    }

    public async Task<IEnumerable<ApiKey>> ListSharedApiKeysAsync()
    {
        return await dbService.GetSharedApiKeysAsync();
    }

    public async Task<bool> RevokeApiKeyAsync(Guid keyId, string userId)
    {
        var key = await dbService.GetApiKeyAsync(keyId);
        if (key == null) return false;

        // User can only delete their own keys
        if (key.UserId != userId)
        {
            // TODO: Allow admin override when implemented
            logger.LogWarning("User {UserId} attempted to revoke key {KeyId} owned by {OwnerId}", userId, keyId, key.UserId);
            return false;
        }

        await dbService.DeleteApiKeyAsync(key);
        return true;
    }

    public async Task<ApiKeyUpdateResult> UpdateApiKeyAsync(Guid keyId, string requestingUserId, string description,
        bool isShared)
    {
        var key = await dbService.GetApiKeyAsync(keyId);
        if (key == null) return new ApiKeyUpdateResult(ApiKeyUpdateStatus.NotFound, null);

        if (key.UserId != requestingUserId)
        {
            logger.LogWarning("User {UserId} attempted to update key {KeyId} owned by {OwnerId}", requestingUserId,
                keyId, key.UserId);
            return new ApiKeyUpdateResult(ApiKeyUpdateStatus.Forbidden, null);
        }

        key.Description = description;
        key.IsShared = isShared;

        await dbService.UpdateApiKeyAsync(key);

        return new ApiKeyUpdateResult(ApiKeyUpdateStatus.Updated, key);
    }

    public async Task<User> GetOrCreateOidcUserAsync(string email, string provider, string providerId)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("OIDC login requires a non-empty email address.");
        }

        var canonicalEmail = CanonicalizeEmail(email);
        var matchingUsers = await dbService.GetUsersByEmailCaseInsensitiveAsync(canonicalEmail);
        if (matchingUsers.Count > 1)
        {
            throw new InvalidOperationException(
                $"The email '{canonicalEmail}' matches multiple legacy user records. Manual account linking is required.");
        }

        var user = matchingUsers.Count == 0 ? null : matchingUsers[0];
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = canonicalEmail,
                Provider = provider,
                ProviderId = providerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbService.AddUserAsync(user);
            return user;
        }

        if (!string.Equals(user.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(user.ProviderId, providerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The email '{canonicalEmail}' is already linked to a different identity. Manual account linking is required.");
        }

        return user;
    }

    public async Task<User> GetOrCreateUserAsync(string email, string provider, string providerId)
    {
        var user = await dbService.GetUserByEmailAsync(email);
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                Provider = provider,
                ProviderId = providerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbService.AddUserAsync(user);
        }
        return user; // Return existing or new
    }

    public async Task<User?> GetUserByIdAsync(string id)
    {
        return await dbService.GetUserByIdAsync(id);
    }

    private static string CanonicalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private string HashToken(string password)
    {
        // Salt is randomly generated, but for stateless verification we usually store salt with hash.
        // Simpler Argon2 wrappers often handle "$argon2id$..." format strings containing params, salt, and hash.
        // Konscious.Security.Cryptography is low level.
        // Let's use a standard format: SALT(16b) + HASH(32b) -> Base64

        var salt = RandomNumberGenerator.GetBytes(16);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = 2; // Core count
        argon2.MemorySize = 65536; // 64 MB
        argon2.Iterations = 4;

        var hash = argon2.GetBytes(32);

        // Return format: {salt_base64}${hash_base64}
        return $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private bool VerifyHash(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 2) return false;

            var salt = Convert.FromBase64String(parts[0]);
            var hash = Convert.FromBase64String(parts[1]);

            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
            argon2.Salt = salt;
            argon2.DegreeOfParallelism = 2;
            argon2.MemorySize = 65536;
            argon2.Iterations = 4;

            var newHash = argon2.GetBytes(32);
            return CryptographicOperations.FixedTimeEquals(hash, newHash);
        }
        catch
        {
            return false;
        }
    }
}

public enum ApiKeyUpdateStatus
{
    Updated,
    NotFound,
    Forbidden
}

public record ApiKeyUpdateResult(ApiKeyUpdateStatus Status, ApiKey? Key);

public record ApiKeyValidationResult(ApiKey? Key, bool IsExpired);
