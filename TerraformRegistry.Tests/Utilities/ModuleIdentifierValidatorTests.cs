using TerraformRegistry.API.Utilities;

namespace TerraformRegistry.Tests.Utilities;

public class ModuleIdentifierValidatorTests
{
    [Theory]
    [InlineData("team")]
    [InlineData("terraform-aws-vpc")]
    [InlineData("azure_rm")]
    [InlineData("a1-b2_c3")]
    public void IsValidSegmentAcceptsSafeSegments(string value)
    {
        Assert.True(ModuleIdentifierValidator.IsValidSegment(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("outside/path")]
    [InlineData("outside\\path")]
    [InlineData("bad.name")]
    [InlineData("-starts-with-dash")]
    [InlineData("_starts_with_underscore")]
    public void IsValidSegmentRejectsUnsafeSegments(string value)
    {
        Assert.False(ModuleIdentifierValidator.IsValidSegment(value));
    }

    [Fact]
    public void GetModuleCoordinateErrorReturnsNullForValidCoordinates()
    {
        var error = ModuleIdentifierValidator.GetModuleCoordinateError("team", "terraform-aws-vpc", "aws");

        Assert.Null(error);
    }

    [Fact]
    public void GetModuleCoordinateErrorIdentifiesInvalidNamespace()
    {
        var error = ModuleIdentifierValidator.GetModuleCoordinateError("../team", "vpc", "aws");

        Assert.Equal(
            "Invalid namespace. Use letters, numbers, hyphens, or underscores; start with a letter or number.",
            error);
    }
}
