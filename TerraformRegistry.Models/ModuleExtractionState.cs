namespace TerraformRegistry.Models;

public class ModuleExtractionState
{
    public string Status { get; set; } = "pending";
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime? LastAttemptedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public string? Error { get; set; }
}
