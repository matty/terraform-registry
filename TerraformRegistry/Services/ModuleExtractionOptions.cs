namespace TerraformRegistry.Services;

public class ModuleExtractionOptions
{
    public bool Enabled { get; set; } = true;
    public string ToolPath { get; set; } = "terraform-config-inspect";
    public int TimeoutSeconds { get; set; } = 15;
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "terraform-registry-extraction");
    public int StartupBackfillBatchSize { get; set; } = 25;
}
