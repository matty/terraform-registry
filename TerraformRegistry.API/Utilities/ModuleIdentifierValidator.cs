namespace TerraformRegistry.API.Utilities;

public static class ModuleIdentifierValidator
{
    private const string Guidance = "Use letters, numbers, hyphens, or underscores; start with a letter or number.";

    public static bool IsValidSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!IsAsciiLetterOrDigit(value[0])) return false;

        foreach (var c in value)
        {
            if (!IsAsciiLetterOrDigit(c) && c != '-' && c != '_') return false;
        }

        return true;
    }

    public static string? GetModuleCoordinateError(string? @namespace, string? name, string? provider)
    {
        if (!IsValidSegment(@namespace)) return $"Invalid namespace. {Guidance}";
        if (!IsValidSegment(name)) return $"Invalid module name. {Guidance}";
        if (!IsValidSegment(provider)) return $"Invalid provider. {Guidance}";
        return null;
    }

    private static bool IsAsciiLetterOrDigit(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}
