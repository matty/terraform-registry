using System.Diagnostics.Metrics;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class OperationalMetricsTests
{
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
