using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class ModuleDownloadAnalyticsBufferTests
{
    [Fact]
    public async Task TryEnqueueRejectsARecordWhenTheBoundedQueueIsFull()
    {
        using var queue = new ModuleDownloadAnalyticsBuffer(Options.Create(new DownloadAnalyticsOptions { Capacity = 1 }));
        var first = new ModuleDownloadRecord("hashicorp", "vpc", "aws", "1.0.0", "127.0.0.1", "terraform");
        var second = new ModuleDownloadRecord("hashicorp", "consul", "aws", "1.0.0", "127.0.0.1", "terraform");

        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(second));

        await foreach (var actual in queue.ReadAllAsync(CancellationToken.None))
        {
            Assert.Equal(first, actual);
            break;
        }
    }

    [Fact]
    public void TryEnqueueRecordsADropMetricWhenTheBoundedQueueIsFull()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "TerraformRegistry.Analytics" &&
                instrument.Name == "terraform_registry.analytics.download_events_dropped")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();
        using var queue = new ModuleDownloadAnalyticsBuffer(Options.Create(new DownloadAnalyticsOptions { Capacity = 1 }));

        Assert.True(queue.TryEnqueue(new ModuleDownloadRecord("hashicorp", "vpc", "aws", "1.0.0", null, null)));
        Assert.False(queue.TryEnqueue(new ModuleDownloadRecord("hashicorp", "consul", "aws", "1.0.0", null, null)));

        Assert.Equal([1L], measurements);
    }
}
