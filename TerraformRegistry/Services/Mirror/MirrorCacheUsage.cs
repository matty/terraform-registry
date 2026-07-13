using System.Collections.Concurrent;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorCacheUsage
{
    private readonly ConcurrentDictionary<string, int> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _moduleCoordinatesByTokenPath = new(StringComparer.Ordinal);

    public IDisposable Acquire(string key)
    {
        _active.AddOrUpdate(key, 1, static (_, count) => checked(count + 1));
        return new Lease(this, key);
    }

    public bool IsInUse(string key) => _active.ContainsKey(key);

    public void RegisterModuleTokenPath(string tokenPath, string coordinate) =>
        _moduleCoordinatesByTokenPath[tokenPath] = coordinate;

    public IDisposable? TryAcquireModuleTokenPath(string tokenPath) =>
        _moduleCoordinatesByTokenPath.TryGetValue(tokenPath, out var coordinate) ? Acquire(coordinate) : null;

    private void Release(string key)
    {
        while (_active.TryGetValue(key, out var count))
        {
            if (count == 1)
            {
                if (_active.TryRemove(new KeyValuePair<string, int>(key, count))) return;
            }
            else if (_active.TryUpdate(key, count - 1, count))
            {
                return;
            }
        }
    }

    private sealed class Lease(MirrorCacheUsage owner, string key) : IDisposable
    {
        private MirrorCacheUsage? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(key);
    }
}
