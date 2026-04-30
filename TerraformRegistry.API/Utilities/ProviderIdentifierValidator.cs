namespace TerraformRegistry.API.Utilities;

public static class ProviderIdentifierValidator
{
    private const string Guidance = "Use lowercase letters, numbers, or hyphens; start with a letter or number.";

    public static bool IsValidProviderSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!IsAsciiLowerLetterOrDigit(value[0])) return false;

        foreach (var c in value)
        {
            if (!IsAsciiLowerLetterOrDigit(c) && c != '-') return false;
        }

        return true;
    }

    public static string? GetProviderCoordinateError(string? @namespace, string? type)
    {
        if (!IsValidProviderSegment(@namespace)) return $"Invalid provider namespace. {Guidance}";
        if (!IsValidProviderSegment(type)) return $"Invalid provider type. {Guidance}";
        return null;
    }

    private static bool IsAsciiLowerLetterOrDigit(char c)
    {
        return c is >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
