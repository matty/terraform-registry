namespace TerraformRegistry.Models;

public sealed record SyncVcsSourceResult(
    string Status,
    int PublishedCount,
    int SkippedCount,
    string? LatestVersion,
    string? Error);
