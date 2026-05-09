namespace TerraformRegistry.Models;

public class ModuleLlmContextState
{
    public string Status { get; set; } = "pending";
    public DateTime? LastAttemptedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public string? Error { get; set; }
}
