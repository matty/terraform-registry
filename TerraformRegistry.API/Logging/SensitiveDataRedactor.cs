using System.Text.RegularExpressions;

namespace TerraformRegistry.API.Logging;

/// <summary>Removes credential-bearing query and key/value values before they are logged.</summary>
public static partial class SensitiveDataRedactor
{
    [GeneratedRegex("(?i)(?:authorization|token|pat|api[_-]?key|webhook[_-]?secret|client[_-]?secret|code|sig|se|sp|sv)=([^&\\s]+)")]
    private static partial Regex SensitiveQueryValue();

    [GeneratedRegex("(?i)bearer\\s+[a-z0-9._~-]+")]
    private static partial Regex BearerValue();

    [GeneratedRegex("(?i)https?://[^\\s]*[?&](?:token|sig|api[_-]?key|code)=[^&\\s]+[^\\s]*")]
    private static partial Regex SignedUrl();

    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var withoutSignedUrls = SignedUrl().Replace(value, "[REDACTED-SIGNED-URL]");
        var withoutQueryValues = SensitiveQueryValue().Replace(withoutSignedUrls, static match =>
            $"{match.Value[..match.Value.IndexOf('=')]}=[REDACTED]");
        return BearerValue().Replace(withoutQueryValues, "Bearer [REDACTED]");
    }

    public static T RedactValue<T>(T value) => value is string text ? (T)(object)Redact(text) : value;
}
