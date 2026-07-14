using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TerraformRegistry.API.Logging;

/// <summary>Removes credential-bearing query and key/value values before they are logged.</summary>
public static partial class SensitiveDataRedactor
{
    [GeneratedRegex("\\b(?<key>authorization|token|pat|api[_-]?key|webhook[_-]?secret|client[_-]?secret|code|sig|se|sp|sv)\\s*(?<separator>[:=])\\s*(?<bearer>bearer\\s+)?(?<value>[^&\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.NonBacktracking)]
    private static partial Regex SensitiveValue();

    [GeneratedRegex("https?://[^\\s]*[?&](?:token|sig|api[_-]?key|code)=[^&\\s]+[^\\s]*", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex SignedUrl();

    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var withoutSignedUrls = SignedUrl().Replace(value, "[REDACTED-SIGNED-URL]");
        return SensitiveValue().Replace(withoutSignedUrls, static match =>
            $"{match.Groups["key"].Value}{match.Groups["separator"].Value}"
            + (match.Groups["bearer"].Success ? "Bearer " : string.Empty)
            + "[REDACTED]");
    }

    public static T RedactValue<T>(T value)
    {
        if (value is not string text)
            return value;

        var redacted = Redact(text);
        return Unsafe.As<string, T>(ref redacted);
    }

    public static Exception? RedactException(Exception? exception)
    {
        if (exception is null)
            return null;

        return new SanitizedLogException(
            exception.GetType().Name,
            Redact(exception.Message),
            RedactException(exception.InnerException));
    }

    private sealed class SanitizedLogException(string exceptionType, string message, Exception? innerException)
        : Exception($"{exceptionType}: {message}", innerException);
}
