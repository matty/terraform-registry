namespace TerraformRegistry.Models;

public sealed class ModuleExtractionAdminSummary
{
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
    public int Processing { get; set; }
    public int NeverExtracted { get; set; }
    public int Total { get; set; }
}

public sealed class ModuleExtractionAdminQuery
{
    public string? Status { get; set; }
    public string? Q { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public sealed class ModuleExtractionAdminPage
{
    public List<ModuleExtractionAdminListItem> Items { get; set; } = [];
    public int Total { get; set; }
}

public class ModuleExtractionAdminListItem
{
    public required string Namespace { get; set; }
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public required string Version { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? LastAttemptedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public string? Error { get; set; }
    public ModuleDocumentationSummary? Documentation { get; set; }
}

public sealed class ModuleExtractionAdminDetail : ModuleExtractionAdminListItem
{
    public ModuleExtractionDocument? Document { get; set; }
}
