using Microsoft.AspNetCore.Http;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;

namespace TerraformRegistry.Tests.UnitTests;

public class AnalyticsHandlersTests
{
    [Fact]
    public async Task GetTopModulesClampsLimitBeforeCallingService()
    {
        var analytics = new CapturingAnalyticsService();

        await AnalyticsHandlers.GetTopModules(analytics, new DefaultHttpContext(), limit: 500);

        Assert.Equal(100, analytics.TopModulesLimit);
    }

    private sealed class CapturingAnalyticsService : IAnalyticsService
    {
        public int TopModulesLimit { get; private set; }

        public Task<DownloadSummary> GetDownloadSummaryAsync() =>
            Task.FromResult(new DownloadSummary(0, 0, 0, 0, 0));

        public Task<TopModulesResult> GetTopModulesAsync(int limit, string period)
        {
            TopModulesLimit = limit;
            return Task.FromResult(new TopModulesResult(period, []));
        }

        public Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval) =>
            Task.FromResult(new DownloadTrendsResult(period, interval, []));

        public Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(
            string moduleNamespace,
            string name,
            string provider,
            string period) =>
            Task.FromResult<ModuleAnalyticsResult?>(null);
    }
}
