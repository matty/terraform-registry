using System.Globalization;
using Npgsql;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlAnalyticsService : IAnalyticsService
{
    private readonly string _connectionString;

    public PostgreSqlAnalyticsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<DownloadSummary> GetDownloadSummaryAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT
                COUNT(*) AS total_downloads,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE) AS downloads_today,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE - INTERVAL '7 days') AS downloads_this_week,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE - INTERVAL '30 days') AS downloads_this_month,
                COUNT(DISTINCT (namespace, name, provider)) AS unique_modules
            FROM module_downloads";

        await using var cmd = new NpgsqlCommand(sql, connection);
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
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var periodStart = GetPeriodStart(period);
        var periodFilter = period == "all" ? "" : "WHERE download_time >= @periodStart";

        var sql = $@"
            SELECT namespace, name, provider, COUNT(*) AS downloads
            FROM module_downloads
            {periodFilter}
            GROUP BY namespace, name, provider
            ORDER BY downloads DESC
            LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@limit", limit);
        if (period != "all")
        {
            cmd.Parameters.AddWithValue("@periodStart", periodStart);
        }

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
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var periodStart = GetPeriodStart(period);
        var periodFilter = period == "all" ? "" : "WHERE download_time >= @periodStart";

        var safeTrunc = interval switch
        {
            "day" => "day",
            "week" => "week",
            "month" => "month",
            _ => "day"
        };

        var sql = $@"
            SELECT DATE_TRUNC('{safeTrunc}', download_time)::date AS date, COUNT(*) AS downloads
            FROM module_downloads
            {periodFilter}
            GROUP BY date
            ORDER BY date";

        await using var cmd = new NpgsqlCommand(sql, connection);
        if (period != "all")
        {
            cmd.Parameters.AddWithValue("@periodStart", periodStart);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        var data = new List<TrendEntry>();
        while (await reader.ReadAsync())
        {
            data.Add(new TrendEntry(
                reader.GetDateTime(0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt64(1)
            ));
        }

        return new DownloadTrendsResult(period, interval, data);
    }

    public async Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string moduleNamespace, string name, string provider, string period)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var periodStart = GetPeriodStart(period);
        var periodFilter = period == "all" ? "" : "AND download_time >= @periodStart";

        // Total downloads
        var totalSql = $@"
            SELECT COUNT(*) FROM module_downloads
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider
            {periodFilter}";

        await using var totalCmd = new NpgsqlCommand(totalSql, connection);
        totalCmd.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        totalCmd.Parameters.AddWithValue("@name", name);
        totalCmd.Parameters.AddWithValue("@provider", provider);
        if (period != "all")
        {
            totalCmd.Parameters.AddWithValue("@periodStart", periodStart);
        }

        var total = (long)(await totalCmd.ExecuteScalarAsync())!;
        if (total == 0)
        {
            return null;
        }

        // Per-version breakdown
        var versionSql = $@"
            SELECT version, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider
            {periodFilter}
            GROUP BY version
            ORDER BY downloads DESC";

        await using var versionCmd = new NpgsqlCommand(versionSql, connection);
        versionCmd.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        versionCmd.Parameters.AddWithValue("@name", name);
        versionCmd.Parameters.AddWithValue("@provider", provider);
        if (period != "all")
        {
            versionCmd.Parameters.AddWithValue("@periodStart", periodStart);
        }

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
            SELECT download_time::date AS date, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = @moduleNamespace AND name = @name AND provider = @provider
            {periodFilter}
            GROUP BY date
            ORDER BY date";

        await using var trendCmd = new NpgsqlCommand(trendSql, connection);
        trendCmd.Parameters.AddWithValue("moduleNamespace", moduleNamespace);
        trendCmd.Parameters.AddWithValue("@name", name);
        trendCmd.Parameters.AddWithValue("@provider", provider);
        if (period != "all")
        {
            trendCmd.Parameters.AddWithValue("@periodStart", periodStart);
        }

        await using var trendReader = await trendCmd.ExecuteReaderAsync();
        var trend = new List<TrendEntry>();
        while (await trendReader.ReadAsync())
        {
            trend.Add(new TrendEntry(
                trendReader.GetDateTime(0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                trendReader.GetInt64(1)
            ));
        }

        return new ModuleAnalyticsResult(moduleNamespace, name, provider, total, versions, trend);
    }

    private static DateTime GetPeriodStart(string period) => period switch
    {
        "7d" => DateTime.UtcNow.AddDays(-7),
        "30d" => DateTime.UtcNow.AddDays(-30),
        "90d" => DateTime.UtcNow.AddDays(-90),
        "all" => DateTime.MinValue,
        _ => DateTime.UtcNow.AddDays(-30)
    };
}
