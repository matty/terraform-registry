using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorPackageUrlSigner
{
    private static readonly string[] PlaceholderKeys =
    [
        "default-auth-token",
        "default-jwt-secret-key",
        "your-secret-key",
        "your-256-bit-secret-key-here-minimum-32-chars",
        "development-only-jwt-secret-key-32-chars-minimum",
        "change-me",
        "changeme",
        "dev-secret-key"
    ];

    private readonly byte[] _key;

    public MirrorPackageUrlSigner(IConfiguration configuration, IHostEnvironment environment)
    {
        var key = configuration["Mirror:PackageUrlSigningKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            key = configuration["Oidc:JwtSecretKey"];
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "Mirror package URL signing requires Mirror:PackageUrlSigningKey or a valid Oidc:JwtSecretKey of at least 32 characters.");
        }

        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Test") &&
            PlaceholderKeys.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Mirror package URL signing key uses a known placeholder value. Configure a unique secret before running outside Development/Test.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public string CreateSignedPackageUrl(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        string filename,
        DateTimeOffset expiresAt)
    {
        var expires = expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Sign(hostname, providerNamespace, type, version, os, arch, filename, expires);
        var path = $"/mirror/providers/{Uri.EscapeDataString(hostname)}/{Uri.EscapeDataString(providerNamespace)}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(filename)}";
        return QueryHelpers.AddQueryString(path, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["version"] = version,
            ["os"] = os,
            ["arch"] = arch,
            ["expires"] = expires,
            ["signature"] = signature
        });
    }

    public bool TryValidate(string signedUrl, DateTimeOffset now, out MirrorPackageUrlClaims claims)
    {
        claims = default;
        if (!Uri.TryCreate(signedUrl, UriKind.RelativeOrAbsolute, out var uri))
        {
            return false;
        }

        if (!uri.IsAbsoluteUri)
        {
            uri = new Uri(new Uri("http://localhost"), uri);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 6 ||
            !string.Equals(segments[0], "mirror", StringComparison.Ordinal) ||
            !string.Equals(segments[1], "providers", StringComparison.Ordinal))
        {
            return false;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!TryGetSingle(query, "version", out var version) ||
            !TryGetSingle(query, "os", out var os) ||
            !TryGetSingle(query, "arch", out var arch) ||
            !TryGetSingle(query, "expires", out var expires) ||
            !TryGetSingle(query, "signature", out var signature) ||
            !long.TryParse(expires, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expiresUnix))
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        if (expiresAt <= now)
        {
            return false;
        }

        var hostname = Uri.UnescapeDataString(segments[2]);
        var providerNamespace = Uri.UnescapeDataString(segments[3]);
        var type = Uri.UnescapeDataString(segments[4]);
        var filename = Uri.UnescapeDataString(segments[5]);
        var expected = Sign(hostname, providerNamespace, type, version, os, arch, filename, expires);
        if (!FixedTimeEquals(signature, expected))
        {
            return false;
        }

        claims = new MirrorPackageUrlClaims(
            hostname,
            providerNamespace,
            type,
            version,
            os,
            arch,
            filename,
            expiresAt);
        return true;
    }

    private string Sign(
        string hostname,
        string providerNamespace,
        string type,
        string version,
        string os,
        string arch,
        string filename,
        string expires)
    {
        var payload = string.Join('\n', hostname, providerNamespace, type, version, os, arch, filename, expires);
        using var hmac = new HMACSHA256(_key);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool TryGetSingle(
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!query.TryGetValue(key, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }

        value = values[0]!;
        return true;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public readonly record struct MirrorPackageUrlClaims(
    string Hostname,
    string Namespace,
    string Type,
    string Version,
    string Os,
    string Arch,
    string Filename,
    DateTimeOffset ExpiresAt);
