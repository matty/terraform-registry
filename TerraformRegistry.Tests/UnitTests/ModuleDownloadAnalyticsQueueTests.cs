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
}
