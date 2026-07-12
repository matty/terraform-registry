using TerraformRegistry.Startup;

namespace TerraformRegistry.Tests.UnitTests;

public class UserAdmissionOptionsTests
{
    [Fact]
    public void NewUsersAreActiveByDefault()
    {
        var user = new TerraformRegistry.Models.User();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void DefaultsToClosedAdmission()
    {
        var options = new UserAdmissionOptions();

        Assert.Equal(UserAdmissionMode.Closed, options.Mode);
        options.Validate();
    }

    [Fact]
    public void AutoProvisionRequiresAtLeastOneConstraint()
    {
        var options = new UserAdmissionOptions
        {
            Mode = UserAdmissionMode.ConstrainedAutoProvision
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("constraint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoProvisionAcceptsAnAllowlistedEmail()
    {
        var options = new UserAdmissionOptions
        {
            Mode = UserAdmissionMode.ConstrainedAutoProvision,
            AllowedEmails = ["user@example.com"]
        };

        options.Validate();
        Assert.True(options.Allows("github", "tenant-a", "user@example.com", emailVerified: true));
    }

    [Fact]
    public void ConfiguredIssuerTenantDomainAndVerifiedEmailMustAllMatch()
    {
        var options = new UserAdmissionOptions
        {
            Mode = UserAdmissionMode.ConstrainedAutoProvision,
            AllowedIssuers = ["https://issuer.example"],
            AllowedTenants = ["tenant-a"],
            AllowedDomains = ["example.com"]
        };

        Assert.True(options.Allows("https://issuer.example", "tenant-a", "user@example.com", emailVerified: true));
        Assert.False(options.Allows("", "tenant-a", "user@example.com", emailVerified: true));
        Assert.False(options.Allows("https://issuer.example", "", "user@example.com", emailVerified: true));
        Assert.False(options.Allows("https://issuer.example", "tenant-a", "user@other.example", emailVerified: true));
        Assert.False(options.Allows("https://issuer.example", "tenant-a", "user@example.com", emailVerified: false));
    }
}
