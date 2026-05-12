using TerraformRegistry.API.Utilities;

namespace TerraformRegistry.Tests.Utilities;

public class ProviderIdentifierValidatorTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-cloud")]
    [InlineData("a1")]
    public void IsValidProviderSegmentAcceptsTerraformProviderSegments(string value)
    {
        Assert.True(ProviderIdentifierValidator.IsValidProviderSegment(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-acme")]
    [InlineData("Acme")]
    [InlineData("acme_cloud")]
    [InlineData("acme.cloud")]
    public void IsValidProviderSegmentRejectsInvalidProviderSegments(string value)
    {
        Assert.False(ProviderIdentifierValidator.IsValidProviderSegment(value));
    }

    [Fact]
    public void GetProviderCoordinateErrorReturnsNullForValidCoordinate()
    {
        Assert.Null(ProviderIdentifierValidator.GetProviderCoordinateError("acme", "example"));
    }

    [Fact]
    public void GetProviderCoordinateErrorReturnsMessageForInvalidType()
    {
        var error = ProviderIdentifierValidator.GetProviderCoordinateError("acme", "Example");

        Assert.Equal(
            "Invalid provider type. Use lowercase letters, numbers, or hyphens; start with a letter or number.",
            error);
    }
}
