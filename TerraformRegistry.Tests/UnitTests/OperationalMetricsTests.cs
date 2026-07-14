using System.Diagnostics.Metrics;
using TerraformRegistry.API.Telemetry;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class OperationalMetricsTests
{
    [Fact]
    public void PaginatedDatabaseMetricReportsDurationAndReturnedRowsWithBoundedTags()
    {
        using var listener = new MeterListener();
        var measurements = new List<(string Name, long Value, string? Backend)>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value,
                tags.ToArray().FirstOrDefault(tag => tag.Key == "backend").Value?.ToString())));
        listener.Start();

        OperationalDatabaseMetrics.RecordModulePage("sqlite", TimeSpan.FromMilliseconds(12), 4);

        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.database.paginated_list.duration_ms" && measurement.Backend == "sqlite");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.database.paginated_list.returned_rows" && measurement.Value == 4 && measurement.Backend == "sqlite");
    }

    [Fact]
    public void CompletionMetricsUseOnlyBoundedTagsAndReportObservableQueueDepth()
    {
        using var listener = new MeterListener();
        var measurements = new List<(string Name, long Value, string? Outcome, string? Kind)>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagValues = tags.ToArray();
            measurements.Add((instrument.Name, value,
                tagValues.FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString(),
                tagValues.FirstOrDefault(tag => tag.Key == "kind").Value?.ToString()));
        });
        listener.Start();

        using var metrics = new OperationalMetrics();
        metrics.RecordExtractionQueueDepth(3);
        metrics.RecordAuthenticationDecision("denied_inactive_user");
        metrics.RecordMirrorUpstreamRequest("provider");
        metrics.RecordMirrorNegativeCacheHit("module");
        metrics.RecordMirrorLeaseLoss("provider");
        metrics.RecordMirrorCacheBytes(1024);
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.extraction.queue_depth" && measurement.Value == 3);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.authentication.decisions" && measurement.Outcome == "denied_inactive_user");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.upstream_requests" && measurement.Kind == "provider");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.negative_cache_hits" && measurement.Kind == "module");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.lease_losses" && measurement.Kind == "provider");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.mirror.cache_bytes" && measurement.Value == 1024);
    }

    [Fact]
    public void OutboxFailureMetricContainsOnlyTheFailureCategory()
    {
        using var listener = new MeterListener();
        var measurements = new List<(string Name, string? Outcome, string? Secret)>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagValues = tags.ToArray();
            measurements.Add((instrument.Name, tagValues.FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString(),
                tagValues.FirstOrDefault(tag => tag.Key == "secret").Value?.ToString()));
        });
        listener.Start();

        using var metrics = new OperationalMetrics();
        metrics.RecordOutboxFailure("delivery_failed");

        Assert.Contains(measurements, measurement =>
            measurement.Name == "terraform_registry.outbox.failures" &&
            measurement.Outcome == "delivery_failed" && measurement.Secret is null);
    }
}

internal sealed class OperationalMetricsTestListener : IDisposable
{
    private readonly MeterListener _listener = new();

    public List<(string Name, string? Outcome)> Measurements { get; } = [];

    public OperationalMetricsTestListener()
    {
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            Measurements.Add((instrument.Name,
                tags.ToArray().FirstOrDefault(tag => tag.Key == "outcome").Value?.ToString())));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
