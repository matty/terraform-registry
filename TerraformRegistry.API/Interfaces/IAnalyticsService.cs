namespace TerraformRegistry.API.Interfaces;

public interface IAnalyticsService
{
    Task<DownloadSummary> GetDownloadSummaryAsync();
    Task<TopModulesResult> GetTopModulesAsync(int limit, string period);
    Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval);
    Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string @namespace, string name, string provider, string period);
}

public record DownloadSummary(long TotalDownloads, long DownloadsToday, long DownloadsThisWeek, long DownloadsThisMonth, long UniqueModules);
public record TopModuleEntry(string Namespace, string Name, string Provider, long Downloads);
public record TopModulesResult(string Period, IReadOnlyList<TopModuleEntry> Modules);
public record TrendEntry(string Date, long Downloads);
public record DownloadTrendsResult(string Period, string Interval, IReadOnlyList<TrendEntry> Data);
public record VersionDownloads(string Version, long Downloads);
public record ModuleAnalyticsResult(string Namespace, string Name, string Provider, long TotalDownloads, IReadOnlyList<VersionDownloads> Versions, IReadOnlyList<TrendEntry> Trend);
