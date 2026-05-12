using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TerraformRegistry.Services;

public class InMemoryTerraformAuthorizationCodeStore(TerraformLoginOptions options) : ITerraformAuthorizationCodeStore
{
    private readonly ConcurrentDictionary<string, TerraformAuthorizationCode> _codes = new(StringComparer.Ordinal);

    public TerraformAuthorizationCode Create(TerraformAuthorizationCodeCreateRequest request)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("=", string.Empty, StringComparison.Ordinal);

        var issued = new TerraformAuthorizationCode(
            code,
            request.UserId,
            request.ClientId,
            request.RedirectUri,
            request.State,
            request.CodeChallenge,
            request.CodeChallengeMethod,
            DateTime.UtcNow.Add(options.AuthorizationCodeLifetime));

        _codes[code] = issued;
        return issued;
    }

    public TerraformAuthorizationCode? Consume(string code, string clientId, string redirectUri)
    {
        if (!_codes.TryRemove(code, out var issued))
        {
            return null;
        }

        if (issued.ExpiresAtUtc < DateTime.UtcNow)
        {
            return null;
        }

        if (!string.Equals(issued.ClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(issued.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            return null;
        }

        return issued;
    }
}
