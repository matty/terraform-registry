using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class SqliteAnalyticsService : IAnalyticsService
{
    private readonly string _connectionString;

    public SqliteAnalyticsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<DownloadSummary> GetDownloadSummaryAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT
                COUNT(*) AS total_downloads,
                COUNT(CASE WHEN download_time >= date('now') THEN 1 END) AS downloads_today,
                COUNT(CASE WHEN download_time >= datetime('now', '-7 days') THEN 1 END) AS downloads_this_week,
                COUNT(CASE WHEN download_time >= datetime('now', '-30 days') THEN 1 END) AS downloads_this_month,
                COUNT(DISTINCT namespace || '/' || name || '/' || provider) AS unique_modules
            FROM module_downloads";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new DownloadSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4)
        );
    }

    public async Task<TopModulesResult> GetTopModulesAsync(int limit, string period)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var periodFilter = GetPeriodFilter(period);
        var whereClause = string.IsNullOrEmpty(periodFilter) ? "" : $"WHERE {periodFilter}";

        var sql = $@"
            SELECT namespace, name, provider, COUNT(*) AS downloads
            FROM module_downloads
            {whereClause}
            GROUP BY namespace, name, provider
            ORDER BY downloads DESC
            LIMIT $limit";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("$limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        var modules = new List<TopModuleEntry>();
        while (await reader.ReadAsync())
        {
            modules.Add(new TopModuleEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3)
            ));
        }

        return new TopModulesResult(period, modules);
    }

    public async Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var periodFilter = GetPeriodFilter(period);
        var whereClause = string.IsNullOrEmpty(periodFilter) ? "" : $"WHERE {periodFilter}";
        var dateGrouping = GetDateGrouping(interval);

        var sql = $@"
            SELECT {dateGrouping} AS date, COUNT(*) AS downloads
            FROM module_downloads
            {whereClause}
            GROUP BY date
            ORDER BY date";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var data = new List<TrendEntry>();
        while (await reader.ReadAsync())
        {
            data.Add(new TrendEntry(
                reader.GetString(0),
                reader.GetInt64(1)
            ));
        }

        return new DownloadTrendsResult(period, interval, data);
    }

    public async Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string @namespace, string name, string provider, string period)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var periodFilter = GetPeriodFilter(period);
        var andPeriod = string.IsNullOrEmpty(periodFilter) ? "" : $"AND {periodFilter}";

        // Total downloads
        var totalSql = $@"
            SELECT COUNT(*) FROM module_downloads
            WHERE namespace = $namespace AND name = $name AND provider = $provider
            {andPeriod}";

        await using var totalCmd = new SqliteCommand(totalSql, connection);
        totalCmd.Parameters.AddWithValue("$namespace", @namespace);
        totalCmd.Parameters.AddWithValue("$name", name);
        totalCmd.Parameters.AddWithValue("$provider", provider);

        var total = (long)(await totalCmd.ExecuteScalarAsync())!;
        if (total == 0)
        {
            return null;
        }

        // Per-version breakdown
        var versionSql = $@"
            SELECT version, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = $namespace AND name = $name AND provider = $provider
            {andPeriod}
            GROUP BY version
            ORDER BY downloads DESC";

        await using var versionCmd = new SqliteCommand(versionSql, connection);
        versionCmd.Parameters.AddWithValue("$namespace", @namespace);
        versionCmd.Parameters.AddWithValue("$name", name);
        versionCmd.Parameters.AddWithValue("$provider", provider);

        await using var versionReader = await versionCmd.ExecuteReaderAsync();
        var versions = new List<VersionDownloads>();
        while (await versionReader.ReadAsync())
        {
            versions.Add(new VersionDownloads(
                versionReader.GetString(0),
                versionReader.GetInt64(1)
            ));
        }

        await versionReader.CloseAsync();

        // Daily trend
        var trendSql = $@"
            SELECT strftime('%Y-%m-%d', download_time) AS date, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = $namespace AND name = $name AND provider = $provider
            {andPeriod}
            GROUP BY date
            ORDER BY date";

        await using var trendCmd = new SqliteCommand(trendSql, connection);
        trendCmd.Parameters.AddWithValue("$namespace", @namespace);
        trendCmd.Parameters.AddWithValue("$name", name);
        trendCmd.Parameters.AddWithValue("$provider", provider);

        await using var trendReader = await trendCmd.ExecuteReaderAsync();
        var trend = new List<TrendEntry>();
        while (await trendReader.ReadAsync())
        {
            trend.Add(new TrendEntry(
                trendReader.GetString(0),
                trendReader.GetInt64(1)
            ));
        }

        return new ModuleAnalyticsResult(@namespace, name, provider, total, versions, trend);
    }

    private static string GetPeriodFilter(string period) => period switch
    {
        "7d" => "download_time >= datetime('now', '-7 days')",
        "30d" => "download_time >= datetime('now', '-30 days')",
        "90d" => "download_time >= datetime('now', '-90 days')",
        "all" => "",
        _ => "download_time >= datetime('now', '-30 days')"
    };

    private static string GetDateGrouping(string interval) => interval switch
    {
        "day" => "strftime('%Y-%m-%d', download_time)",
        "week" => "strftime('%Y-%W', download_time)",
        "month" => "strftime('%Y-%m', download_time)",
        _ => "strftime('%Y-%m-%d', download_time)"
    };
}
