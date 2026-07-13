using System.Collections.Concurrent;
using TerraformRegistry.Startup;

namespace TerraformRegistry.Services;

public sealed class ApiKeyVerificationGate(ApiKeySecurityOptions options, RegistryRateLimitMetrics metrics) : IDisposable
{
    private readonly ConcurrentDictionary<string, PartitionGate> _prefixes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PartitionGate> _principals = new(StringComparer.Ordinal);

    public IDisposable? TryEnterPrefix(string prefix) => TryEnter(_prefixes, prefix, "prefix");

    public IDisposable? TryEnterPrincipal(string userId) => TryEnter(_principals, userId, "principal");

    private Releaser? TryEnter(ConcurrentDictionary<string, PartitionGate> partitions, string key, string partitionCategory)
    {
        var lease = partitions.GetOrAdd(key, _ => new PartitionGate(options)).TryEnter();
        if (lease is null)
        {
            metrics.RecordRejection(RateLimitPolicyNames.ApiKeyVerification, partitionCategory);
        }

        return lease;
    }

    private sealed class PartitionGate(ApiKeySecurityOptions options) : IDisposable
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _concurrency = new(options.MaxConcurrentVerificationsPerPartition);
        private DateTime _windowStartedUtc = DateTime.UtcNow;
        private int _remaining = options.VerificationPermitLimit;

        public Releaser? TryEnter()
        {
            if (!_concurrency.Wait(0))
            {
                return null;
            }

            lock (_sync)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStartedUtc >= TimeSpan.FromSeconds(options.VerificationWindowSeconds))
                {
                    _windowStartedUtc = now;
                    _remaining = options.VerificationPermitLimit;
                }

                if (_remaining > 0)
                {
                    _remaining--;
                    return new Releaser(_concurrency);
                }
            }

            _concurrency.Release();
            return null;
        }

        public void Dispose() => _concurrency.Dispose();
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }

    public void Dispose()
    {
        foreach (var gate in _prefixes.Values)
        {
            gate.Dispose();
        }

        foreach (var gate in _principals.Values)
        {
            gate.Dispose();
        }
    }
}
