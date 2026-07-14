using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

/// <summary>Renews a mirror lease and records ownership loss so callers can fail closed before publishing.</summary>
public sealed class MirrorLeaseHeartbeat : IAsyncDisposable
{
    private readonly IMirrorLeaseService _leaseService;
    private readonly MirrorLeaseHandle _lease;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _firstHeartbeat = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _runner;
    private readonly OperationalMetrics? _metrics;
    private readonly string _kind;
    private int _ownershipLost;

    public MirrorLeaseHeartbeat(IMirrorLeaseService leaseService, MirrorLeaseHandle lease, TimeSpan interval,
        OperationalMetrics? metrics = null, string kind = "unknown")
    {
        _leaseService = leaseService;
        _lease = lease;
        _interval = interval;
        _metrics = metrics;
        _kind = kind;
        _runner = RunAsync();
    }

    public bool IsOwnershipLost => Volatile.Read(ref _ownershipLost) != 0;

    public Task WaitForFirstHeartbeatAsync() => _firstHeartbeat.Task;

    public void ThrowIfOwnershipLost()
    {
        if (IsOwnershipLost)
        {
            throw new InvalidOperationException($"Mirror lease ownership was lost for '{_lease.LeaseKey}'.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _runner;
        _stopping.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                if (_interval > TimeSpan.Zero)
                {
                    await Task.Delay(_interval, _stopping.Token);
                }

                try
                {
                    if (!await _leaseService.HeartbeatAsync(_lease, _stopping.Token))
                    {
                        Interlocked.Exchange(ref _ownershipLost, 1);
                        _metrics?.RecordMirrorLeaseLoss(_kind);
                        return;
                    }
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (TimeoutException)
                {
                    Interlocked.Exchange(ref _ownershipLost, 1);
                    _metrics?.RecordMirrorLeaseLoss(_kind);
                    return;
                }
                catch (System.Data.Common.DbException)
                {
                    Interlocked.Exchange(ref _ownershipLost, 1);
                    _metrics?.RecordMirrorLeaseLoss(_kind);
                    return;
                }
                catch (IOException)
                {
                    Interlocked.Exchange(ref _ownershipLost, 1);
                    _metrics?.RecordMirrorLeaseLoss(_kind);
                    return;
                }
                finally
                {
                    _firstHeartbeat.TrySetResult();
                }

                if (_interval <= TimeSpan.Zero)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stopping.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Disposal deliberately cancels the renewal delay after the fetch completes.
            return;
        }
        finally
        {
            _firstHeartbeat.TrySetResult();
        }
    }
}
