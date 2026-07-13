namespace TerraformRegistry.Startup;

public sealed class ApiKeySecurityOptions
{
    public const string SectionName = "ApiKeySecurity";

    public string DigestKey { get; set; } = string.Empty;
    public int VerificationPermitLimit { get; set; } = 60;
    public int VerificationWindowSeconds { get; set; } = 60;
    public int MaxConcurrentVerificationsPerPartition { get; set; } = 2;
    public int LastUsedUpdateIntervalSeconds { get; set; } = 300;

    public void Validate()
    {
        if (VerificationPermitLimit <= 0 || VerificationWindowSeconds <= 0 ||
            MaxConcurrentVerificationsPerPartition <= 0 || LastUsedUpdateIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("ApiKeySecurity limits must be greater than zero.");
        }
    }

    public void ValidateDigestKey()
    {
        if (DigestKey.Length < 32)
        {
            throw new InvalidOperationException("ApiKeySecurity:DigestKey must be at least 32 characters.");
        }
    }
}
