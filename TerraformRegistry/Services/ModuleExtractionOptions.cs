namespace TerraformRegistry.Services;

public class ModuleExtractionOptions
{
    public const long DefaultMaxArchiveBytes = 100L * 1024L * 1024L;

    public bool Enabled { get; set; } = true;
    public string ToolPath { get; set; } = "terraform-config-inspect";
    public int TimeoutSeconds { get; set; } = 15;
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "terraform-registry-extraction");
    public int StartupBackfillBatchSize { get; set; } = 25;
    public long MaxArchiveBytes { get; set; } = DefaultMaxArchiveBytes;
}
