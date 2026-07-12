namespace TerraformRegistry.Startup;

public enum UserAdmissionMode
{
    Closed,
    ExistingUsersOnly,
    ConstrainedAutoProvision
}

public sealed class UserAdmissionOptions
{
    public const string SectionName = "UserAdmission";

    public UserAdmissionMode Mode { get; set; } = UserAdmissionMode.Closed;
    public string[] AllowedIssuers { get; set; } = [];
    public string[] AllowedTenants { get; set; } = [];
    public string[] AllowedDomains { get; set; } = [];
    public string[] AllowedEmails { get; set; } = [];
    public bool RequireVerifiedEmail { get; set; } = true;

    public void Validate()
    {
        if (Mode != UserAdmissionMode.ConstrainedAutoProvision)
        {
            return;
        }

        if (AllowedIssuers.Length == 0 && AllowedTenants.Length == 0 && AllowedDomains.Length == 0 && AllowedEmails.Length == 0)
        {
            throw new InvalidOperationException(
                "Constrained auto-provisioning requires at least one admission constraint.");
        }
    }

    public bool Allows(string issuer, string tenant, string email, bool emailVerified)
    {
        if (RequireVerifiedEmail && !emailVerified)
        {
            return false;
        }

        var domain = email.LastIndexOf('@') is var at && at >= 0 ? email[(at + 1)..] : string.Empty;
        return Matches(AllowedIssuers, issuer) && Matches(AllowedTenants, tenant) &&
               Matches(AllowedDomains, domain) && Matches(AllowedEmails, email);
    }

    private static bool Matches(IEnumerable<string> constraints, string value)
    {
        var values = constraints.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return values.Length == 0 || values.Contains(value, StringComparer.OrdinalIgnoreCase);
    }
}
