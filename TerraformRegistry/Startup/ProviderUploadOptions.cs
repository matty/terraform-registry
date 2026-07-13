namespace TerraformRegistry.Startup;

public sealed class ProviderUploadOptions
{
    public const string SectionName = "ProviderUpload";
    public long MaxPackageBytes { get; set; } = 536_870_912;
    public string TempRoot { get; set; } = Path.GetTempPath();

    public void Validate()
    {
        if (MaxPackageBytes <= 0)
        {
            throw new InvalidOperationException("ProviderUpload:MaxPackageBytes must be greater than zero.");
        }
    }
}
