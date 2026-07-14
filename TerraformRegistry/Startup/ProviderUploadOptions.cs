using TerraformRegistry.Services;

namespace TerraformRegistry.Startup;

public sealed class ProviderUploadOptions
{
    public const string SectionName = "ProviderUpload";
    public long MaxPackageBytes { get; set; } = 536_870_912;
    public long MaxChecksumBytes { get; set; } = ProviderPackageValidator.DefaultMaxSignatureBytes;
    public string TempRoot { get; set; } = Path.GetTempPath();

    public void Validate()
    {
        if (MaxPackageBytes <= 0 || MaxChecksumBytes <= 0)
        {
            throw new InvalidOperationException("ProviderUpload byte limits must be greater than zero.");
        }
    }
}
