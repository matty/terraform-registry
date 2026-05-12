using TerraformRegistry.API.Utilities;

namespace TerraformRegistry.Tests.Utilities;

public class SemVerValidatorTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("0.1.0", true)]
    [InlineData("10.20.30", true)]
    [InlineData("1.0.0-alpha", true)]
    [InlineData("1.0.0-alpha.1", true)]
    [InlineData("1.0.0-0.3.7", true)]
    [InlineData("1.0.0-x.7.z.92", true)]
    [InlineData("1.0.0-alpha+001", true)]
    [InlineData("1.0.0+20130313144700", true)]
    [InlineData("1.0.0-beta+exp.sha.5114f85", true)]
    [InlineData("1.0.0-rc.1+build.1", true)]
    [InlineData("1.2.3----RC-SNAPSHOT.12.9.1--.12+788", true)] // Example from spec
    [InlineData("1.2.3----R-S.12.9.1--.12+meta", true)] // Example from spec
    [InlineData("1.2.3----RC-SNAPSHOT.12.9.1--.12", true)] // Example from spec
    [InlineData("1.0.0+0.build.1-rc.10000aaa-kk-0.1", true)] // Example from spec
    [InlineData("99999999999999999999999.99999999999999999999999.99999999999999999999999", true)] // Large numbers
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    [InlineData("1.0", false)] // Missing patch
    [InlineData("1", false)] // Missing minor and patch
    [InlineData("1.0.0a", false)] // Invalid char after patch
    [InlineData("1.0.0-", false)] // Empty prerelease identifier
    [InlineData("1.0.0+", false)] // Empty build metadata
    [InlineData("1.0.0-alpha..1", false)] // Double dot in prerelease
    [InlineData("1.0.0-alpha_beta", false)] // Invalid char in prerelease
    [InlineData("1.0.0+build#1", false)] // Invalid char in build metadata
    [InlineData("01.0.0", false)] // Leading zero in major
    [InlineData("1.01.0", false)] // Leading zero in minor
    [InlineData("1.0.01", false)] // Leading zero in patch
    [InlineData("1.0.0-01", false)] // Leading zero in numeric prerelease identifier
    [InlineData("a.b.c", false)] // Non-numeric major/minor/patch
    [InlineData("1.0.0 ", false)] // Trailing space
    [InlineData(" 1.0.0", false)] // Leading space
    public void IsValidReturnsExpectedResult(string? version, bool expected)
    {
        Assert.Equal(expected, SemVerValidator.IsValid(version!));
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null, null)]
    [InlineData("10.20.30-alpha.1", 10, 20, 30, "alpha.1", null)]
    [InlineData("1.0.0+build.123", 1, 0, 0, null, "build.123")]
    [InlineData("2.5.0-rc.2+meta.data", 2, 5, 0, "rc.2", "meta.data")]
    public void TryParseValidVersionReturnsTrueAndCorrectComponents(string version, int expMajor, int expMinor,
        int expPatch, string? expPrerelease, string? expBuild)
    {
        var result = SemVerValidator.TryParse(version, out var major, out var minor, out var patch, out var prerelease,
            out var buildMetadata);

        Assert.True(result);
        Assert.Equal(expMajor, major);
        Assert.Equal(expMinor, minor);
        Assert.Equal(expPatch, patch);
        Assert.Equal(expPrerelease, prerelease);
        Assert.Equal(expBuild, buildMetadata);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("invalid")]
    [InlineData("")]
    // [InlineData(null)] // Cannot use null directly in InlineData for string?
    public void TryParseInvalidVersionReturnsFalse(string version)
    {
        var result = SemVerValidator.TryParse(version, out _, out _, out _, out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParseNullVersionReturnsFalse()
    {
        var result = SemVerValidator.TryParse(null!, out _, out _, out _, out _, out _);
        Assert.False(result);
    }

    [Theory]
    // Equal
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.2.3-alpha", "1.2.3-alpha", 0)]
    [InlineData("1.0.0-rc.1+build.1", "1.0.0-rc.1+build.2", 0)] // Build metadata ignored

    // Major/Minor/Patch comparison
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.0.0", "1.0.1", -1)]

    // Prerelease vs No Prerelease
    [InlineData("1.0.0", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]

    // Prerelease comparison
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", -1)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.1", 1)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta.alpha", -1)] // alpha < beta
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)] // numeric < string
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1", 1)] // string > numeric
    [InlineData("1.0.0-rc.1", "1.0.0-rc.10", -1)] // numeric comparison
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)] // shorter prerelease < longer prerelease
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", 1)] // longer prerelease > shorter prerelease
    [InlineData("1.0.0-1", "1.0.0-2", -1)] // numeric identifiers
    [InlineData("1.0.0-2", "1.0.0-1", 1)] // numeric identifiers
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.1.beta", -1)] // length comparison after common part

    // Invalid versions
    [InlineData("1.0.0", "invalid", null)]
    [InlineData("invalid", "1.0.0", null)]
    [InlineData("invalid", "also.invalid", null)]
    public void CompareReturnsExpectedResult(string version1, string version2, int? expected)
    {
        Assert.Equal(expected, SemVerValidator.Compare(version1, version2));
    }
}
