using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.Models;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Services;

public class ApiKeyService(
    IDatabaseService dbService,
    ILogger<ApiKeyService> logger,
    UserAdmissionOptions userAdmissionOptions,
    ApiKeySecurityOptions securityOptions,
    ApiKeyVerificationGate verificationGate,
    OperationalMetrics? metrics = null) : IApiKeyService
{
    private const int TokenLength = 32;
    // private const string TokenPrefix = "tf-"; // Removed prefix requirement

    public async Task<(string RawToken, ApiKey Key)> CreateApiKeyAsync(string userId, string description, bool isShared = false)
    {
        return await CreateApiKeyInternalAsync(userId, description, isShared, null);
    }

    public async Task<(string RawToken, ApiKey Key)> CreateExpiringApiKeyAsync(string userId, string description,
        DateTime expiresAt, bool isShared = false)
    {
        return await CreateApiKeyInternalAsync(userId, description, isShared, expiresAt);
    }

    private async Task<(string RawToken, ApiKey Key)> CreateApiKeyInternalAsync(string userId, string description,
        bool isShared, DateTime? expiresAt)
    {
        // Generate random token
        var randomBytes = RandomNumberGenerator.GetBytes(TokenLength);
        var tokenCore = Convert.ToBase64String(randomBytes)
            .Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).Replace("=", "", StringComparison.Ordinal); // URL-safe base64
        var rawToken = tokenCore; // No prefix

        var tokenHash = CreateDigest(rawToken);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Description = description,
            TokenHash = tokenHash,
            Prefix = rawToken.Substring(0, 8),
            IsShared = isShared,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        await dbService.AddApiKeyAsync(apiKey);

        RegistryLog.Information(logger, "Created new API key {KeyId} for user {UserId}", apiKey.Id, userId);

        return (rawToken, apiKey);
    }

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            metrics?.RecordAuthenticationDecision("denied_missing_api_key");
            return new ApiKeyValidationResult(null, false);
        }

        var prefix = rawToken.Length >= 8 ? rawToken.Substring(0, 8) : rawToken;

        // Find keys with matching prefix to minimize Argon2 checks (expensive)
        var candidates = await dbService.GetApiKeysByPrefixAsync(prefix);

        foreach (var key in candidates)
        {
            using var prefixLease = verificationGate.TryEnterPrefix(key.Prefix);
            if (prefixLease is null)
            {
                RegistryLog.Warning(logger, "Rejected API key verification because the prefix limit was reached.");
                metrics?.RecordAuthenticationDecision("denied_rate_limited");
                return new ApiKeyValidationResult(null, false, true);
            }

            var isLegacy = !key.TokenHash.StartsWith("v1:", StringComparison.Ordinal);
            if (VerifyToken(rawToken, key.TokenHash))
            {
                using var principalLease = verificationGate.TryEnterPrincipal(key.UserId);
                if (principalLease is null)
                {
                    RegistryLog.Warning(logger, "Rejected API key verification because the principal limit was reached.");
                    metrics?.RecordAuthenticationDecision("denied_rate_limited");
                    return new ApiKeyValidationResult(null, false, true);
                }

                if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
                {
                    metrics?.RecordAuthenticationDecision("denied_expired_api_key");
                    return new ApiKeyValidationResult(null, true);
                }

                var user = await dbService.GetUserByIdAsync(key.UserId);
                if (user?.IsActive != true)
                {
                    RegistryLog.Warning(logger, "Rejected API key for inactive or missing user {UserId}", key.UserId);
                    metrics?.RecordAuthenticationDecision("denied_inactive_user");
                    return new ApiKeyValidationResult(null, false);
                }

                var now = DateTime.UtcNow;
                var upgradeNeeded = isLegacy;
                if (upgradeNeeded)
                {
                    key.TokenHash = CreateDigest(rawToken);
                }

                if (upgradeNeeded || !key.LastUsedAt.HasValue ||
                    now - key.LastUsedAt.Value >= TimeSpan.FromSeconds(securityOptions.LastUsedUpdateIntervalSeconds))
                {
                    key.LastUsedAt = now;
                    await dbService.UpdateApiKeyAsync(key);
                }
                metrics?.RecordAuthenticationDecision("admitted_api_key");
                return new ApiKeyValidationResult(key, false);
            }
        }

        metrics?.RecordAuthenticationDecision("denied_invalid_api_key");
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
            RegistryLog.Warning(logger, "User {UserId} attempted to revoke key {KeyId} owned by {OwnerId}", userId, keyId, key.UserId);
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
            RegistryLog.Warning(logger, "User {UserId} attempted to update key {KeyId} owned by {OwnerId}", requestingUserId,
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
        return await GetOrCreateOidcUserAsync(new OidcUserAdmission(email, provider, providerId, provider, string.Empty, true));
    }

    public async Task<User> GetOrCreateOidcUserAsync(OidcUserAdmission admission)
    {
        if (string.IsNullOrWhiteSpace(admission.Email))
        {
            throw new InvalidOperationException("OIDC login requires a non-empty email address.");
        }

        var canonicalEmail = CanonicalizeEmail(admission.Email);
        var matchingUsers = await dbService.GetUsersByEmailCaseInsensitiveAsync(canonicalEmail);
        if (matchingUsers.Count > 1)
        {
            throw new InvalidOperationException(
                $"The email '{canonicalEmail}' matches multiple legacy user records. Manual account linking is required.");
        }

        var user = matchingUsers.Count == 0 ? null : matchingUsers[0];
        if (user == null)
        {
            if (userAdmissionOptions.Mode != UserAdmissionMode.ConstrainedAutoProvision ||
                !userAdmissionOptions.Allows(admission.Issuer, admission.TenantId, canonicalEmail, admission.EmailVerified))
            {
                throw new InvalidOperationException("OIDC admission policy denied this new identity.");
            }

            user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = canonicalEmail,
                Provider = admission.Provider,
                ProviderId = admission.ProviderId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await dbService.AddUserAsync(user);
            return user;
        }

        if (!string.Equals(user.Provider, admission.Provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(user.ProviderId, admission.ProviderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The email '{canonicalEmail}' is already linked to a different identity. Manual account linking is required.");
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException("The user account is disabled.");
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

    private string CreateDigest(string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(securityOptions.DigestKey));
        return $"v1:{Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)))}";
    }

    private bool VerifyToken(string token, string storedHash)
    {
        if (storedHash.StartsWith("v1:", StringComparison.Ordinal))
        {
            var expected = Encoding.UTF8.GetBytes(CreateDigest(token));
            var actual = Encoding.UTF8.GetBytes(storedHash);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        return VerifyLegacyHash(token, storedHash);
    }

    private static bool VerifyLegacyHash(string password, string storedHash)
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

public record ApiKeyValidationResult(ApiKey? Key, bool IsExpired, bool IsRateLimited = false);
