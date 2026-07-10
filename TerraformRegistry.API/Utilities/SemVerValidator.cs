using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace TerraformRegistry.API.Utilities;

/// <summary>
///     Provides validation and parsing for Semantic Versioning 2.0.0.
///     For full specification, see: https://semver.org/
/// </summary>
public static class SemVerValidator
{
    // SemVer 2.0.0 pattern
    // Major.Minor.Patch[-Prerelease][+BuildMetadata]
    private static readonly Regex SemVerPattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    ///     Determines whether the specified version string is a valid SemVer 2.0.0.
    /// </summary>
    /// <param name="version">The version string to validate.</param>
    /// <returns>True if the string is a valid SemVer; otherwise, false.</returns>
    public static bool IsValid(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        return SemVerPattern.IsMatch(version);
    }

    /// <summary>
    ///     Attempts to parse a version string into its semantic version components.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <param name="major">When this method returns, contains the major version if successful; otherwise, 0.</param>
    /// <param name="minor">When this method returns, contains the minor version if successful; otherwise, 0.</param>
    /// <param name="patch">When this method returns, contains the patch version if successful; otherwise, 0.</param>
    /// <param name="prerelease">When this method returns, contains the prerelease version if present; otherwise, null.</param>
    /// <param name="buildMetadata">When this method returns, contains the build metadata if present; otherwise, null.</param>
    /// <returns>True if the version was successfully parsed; otherwise, false.</returns>
    public static bool TryParse(string version, out int major, out int minor, out int patch,
        out string? prerelease, out string? buildMetadata)
    {
        major = minor = patch = 0;
        prerelease = buildMetadata = null;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        var match = SemVerPattern.Match(version);
        if (!match.Success)
            return false;

        major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        patch = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        if (match.Groups[4].Success)
            prerelease = match.Groups[4].Value;

        if (match.Groups[5].Success)
            buildMetadata = match.Groups[5].Value;

        return true;
    }

    /// <summary>
    ///     Compares two semantic version strings.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <returns>
    ///     A signed integer that indicates the relative values of version1 and version2:
    ///     Less than zero: version1 is less than version2.
    ///     Zero: version1 equals version2.
    ///     Greater than zero: version1 is greater than version2.
    ///     If either version is invalid, returns null.
    /// </returns>
    public static int? Compare(string version1, string version2)
    {
        var match1 = SemVerPattern.Match(version1 ?? string.Empty);
        var match2 = SemVerPattern.Match(version2 ?? string.Empty);
        if (!match1.Success || !match2.Success)
        {
            return null;
        }

        var major1 = BigInteger.Parse(match1.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor1 = BigInteger.Parse(match1.Groups[2].Value, CultureInfo.InvariantCulture);
        var patch1 = BigInteger.Parse(match1.Groups[3].Value, CultureInfo.InvariantCulture);
        var major2 = BigInteger.Parse(match2.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor2 = BigInteger.Parse(match2.Groups[2].Value, CultureInfo.InvariantCulture);
        var patch2 = BigInteger.Parse(match2.Groups[3].Value, CultureInfo.InvariantCulture);
        var prerelease1 = match1.Groups[4].Success ? match1.Groups[4].Value : null;
        var prerelease2 = match2.Groups[4].Success ? match2.Groups[4].Value : null;

        // Compare major.minor.patch
        var result = major1.CompareTo(major2);
        if (result != 0) return result;

        result = minor1.CompareTo(minor2);
        if (result != 0) return result;

        result = patch1.CompareTo(patch2);
        if (result != 0) return result;

        // Pre-release versions have lower precedence than the associated normal version
        if (prerelease1 is null && prerelease2 is null) return 0;
        if (prerelease1 is null) return 1; // 1.0.0 > 1.0.0-alpha
        if (prerelease2 is null) return -1; // 1.0.0-alpha < 1.0.0

        // Compare pre-release identifiers
        return ComparePrerelease(prerelease1, prerelease2);
    }

    private static int ComparePrerelease(string prerelease1, string prerelease2)
    {
        var identifiers1 = prerelease1.Split('.');
        var identifiers2 = prerelease2.Split('.');

        var minLength = Math.Min(identifiers1.Length, identifiers2.Length);

        for (var i = 0; i < minLength; i++)
        {
            var id1 = identifiers1[i];
            var id2 = identifiers2[i];

            var isNum1 = BigInteger.TryParse(id1, NumberStyles.None, CultureInfo.InvariantCulture, out var num1);
            var isNum2 = BigInteger.TryParse(id2, NumberStyles.None, CultureInfo.InvariantCulture, out var num2);

            int result;

            // Numeric identifiers always have lower precedence than non-numeric identifiers
            if (isNum1 && isNum2)
                result = num1.CompareTo(num2);
            else if (isNum1)
                result = -1; // Numeric has lower precedence
            else if (isNum2)
                result = 1; // Non-numeric has higher precedence
            else
                result = string.Compare(id1, id2, StringComparison.Ordinal);

            if (result != 0)
                return result;
        }

        // A larger set of pre-release fields has a higher precedence
        return identifiers1.Length.CompareTo(identifiers2.Length);
    }
}
