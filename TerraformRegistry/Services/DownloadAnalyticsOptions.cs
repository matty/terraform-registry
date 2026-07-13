namespace TerraformRegistry.Services;

public sealed class DownloadAnalyticsOptions
{
    public const string SectionName = "DownloadAnalytics";

    public int Capacity { get; set; } = 10_000;

    public void Validate()
    {
        if (Capacity <= 0)
            throw new InvalidOperationException("Download analytics queue capacity must be greater than zero.");
    }
}
