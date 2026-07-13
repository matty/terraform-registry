using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace TerraformRegistry.Services;

public sealed record ModuleDownloadRecord(
    string Namespace,
    string Name,
    string Provider,
    string Version,
    string? ClientIp,
    string? UserAgent);

public sealed class ModuleDownloadAnalyticsBuffer : IDisposable
{
    private readonly Channel<ModuleDownloadRecord> channel;
    private readonly Meter meter = new("TerraformRegistry.Analytics");
    private readonly Counter<long> dropped;

    public ModuleDownloadAnalyticsBuffer(IOptions<DownloadAnalyticsOptions> options)
    {
        channel = Channel.CreateBounded<ModuleDownloadRecord>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        dropped = meter.CreateCounter<long>("terraform_registry.analytics.download_events_dropped");
    }

    public bool TryEnqueue(ModuleDownloadRecord record)
    {
        if (channel.Writer.TryWrite(record)) return true;

        dropped.Add(1);
        return false;
    }

    public async IAsyncEnumerable<ModuleDownloadRecord> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var record in channel.Reader.ReadAllAsync(cancellationToken))
            yield return record;
    }

    public void Complete() => channel.Writer.TryComplete();

    public void Dispose()
    {
        Complete();
        meter.Dispose();
    }
}
