using TerraformRegistry.API.Utilities;

namespace TerraformRegistry.Tests.Utilities;

public class ModuleIdentifierValidatorTests
{
    [Theory]
    [InlineData("team")]
    [InlineData("terraform-aws-vpc")]
    [InlineData("azure_rm")]
    [InlineData("a1-b2_c3")]
    public void IsValidSegment_AcceptsSafeSegments(string value)
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
    public void IsValidSegment_RejectsUnsafeSegments(string value)
    {
        Assert.False(ModuleIdentifierValidator.IsValidSegment(value));
    }

    [Fact]
    public void GetModuleCoordinateError_ReturnsNullForValidCoordinates()
    {
        var error = ModuleIdentifierValidator.GetModuleCoordinateError("team", "terraform-aws-vpc", "aws");

        Assert.Null(error);
    }

    [Fact]
    public void GetModuleCoordinateError_IdentifiesInvalidNamespace()
    {
        var error = ModuleIdentifierValidator.GetModuleCoordinateError("../team", "vpc", "aws");

        Assert.Equal(
            "Invalid namespace. Use letters, numbers, hyphens, or underscores; start with a letter or number.",
            error);
    }
}
