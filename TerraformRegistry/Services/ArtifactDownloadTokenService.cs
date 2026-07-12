using System.Security.Cryptography;
using System.Text;

namespace TerraformRegistry.Services;

public sealed class ArtifactDownloadTokenService
{
    public const string ProductionPlaceholder = "configure-a-unique-artifact-download-token-key-before-production";

    private readonly byte[] _key;

    public ArtifactDownloadTokenService(IConfiguration configuration)
    {
        var signingKey = configuration["ArtifactDownloadTokens:SigningKey"]
                         ?? throw new InvalidOperationException("ArtifactDownloadTokens:SigningKey is required.");
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("ArtifactDownloadTokens:SigningKey must be at least 32 characters.");
        }

        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string Create(string purpose, string path, TimeSpan lifetime)
    {
        var payload = $"{purpose}\n{DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds()}\n{path}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(_key);
        return $"{Encode(bytes)}.{Encode(hmac.ComputeHash(bytes))}";
    }

    public bool TryValidate(string token, string purpose, out string path)
    {
        path = string.Empty;
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;
        try
        {
            var bytes = Decode(parts[0]);
            var signature = Decode(parts[1]);
            if (!string.Equals(parts[0], Encode(bytes), StringComparison.Ordinal) ||
                !string.Equals(parts[1], Encode(signature), StringComparison.Ordinal))
            {
                return false;
            }
            using var hmac = new HMACSHA256(_key);
            if (!CryptographicOperations.FixedTimeEquals(signature, hmac.ComputeHash(bytes))) return false;
            var values = Encoding.UTF8.GetString(bytes).Split('\n', 3);
            if (values.Length != 3 || values[0] != purpose || !long.TryParse(values[1], out var expiry) ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry)
            {
                return false;
            }
            path = values[2];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
