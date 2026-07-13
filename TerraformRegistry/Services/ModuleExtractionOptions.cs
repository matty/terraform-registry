namespace TerraformRegistry.Services;

public class ModuleExtractionOptions
{
    public const long DefaultMaxArchiveBytes = 100L * 1024L * 1024L;
    public const long DefaultMaxExpandedArchiveBytes = 1L * 1024L * 1024L * 1024L;
    public const int DefaultMaxArchiveEntries = 10_000;
    public const long DefaultMaxExpandedEntryBytes = 256L * 1024L * 1024L;
    public const int DefaultMaxCompressionRatio = 100;

    public bool Enabled { get; set; } = true;
    public string ToolPath { get; set; } = "terraform-config-inspect";
    public int TimeoutSeconds { get; set; } = 15;
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "terraform-registry-extraction");
    public int StartupBackfillBatchSize { get; set; } = 25;
    public int MaxPendingJobs { get; set; } = 1_000;
    public int WorkerConcurrency { get; set; } = 2;
    public int JobLeaseSeconds { get; set; } = 60;
    public int JobRetryLimit { get; set; } = 3;
    public int JobPollIntervalMilliseconds { get; set; } = 500;
    public long MaxArchiveBytes { get; set; } = DefaultMaxArchiveBytes;
    public long MaxExpandedArchiveBytes { get; set; } = DefaultMaxExpandedArchiveBytes;
    public int MaxArchiveEntries { get; set; } = DefaultMaxArchiveEntries;
    public long MaxExpandedEntryBytes { get; set; } = DefaultMaxExpandedEntryBytes;
    public int MaxCompressionRatio { get; set; } = DefaultMaxCompressionRatio;

    public void Validate()
    {
        if (MaxArchiveBytes <= 0 || MaxExpandedArchiveBytes <= 0 || MaxExpandedEntryBytes <= 0 ||
            MaxArchiveEntries <= 0 || MaxCompressionRatio <= 0)
        {
            throw new InvalidOperationException("Archive ingestion limits must all be greater than zero.");
        }

        if (MaxPendingJobs <= 0 || WorkerConcurrency <= 0 || JobLeaseSeconds <= 0 || JobRetryLimit <= 0 ||
            JobPollIntervalMilliseconds <= 0)
        {
            throw new InvalidOperationException("Extraction job limits must all be greater than zero.");
        }
    }
}
