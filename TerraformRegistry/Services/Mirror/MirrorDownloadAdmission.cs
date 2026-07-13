using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

/// <summary>
/// Process-wide admission accounting for upstream mirror downloads. Limits are evaluated at
/// acquisition time so an operator reduction takes effect before any additional fetch starts.
/// </summary>
public sealed class MirrorDownloadAdmission
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _activeByCoordinate = new(StringComparer.Ordinal);
    private int _activeDownloads;

    public MirrorDownloadAdmissionLease? TryAcquire(MirrorLimitRuntimeOptions limits, string coordinate)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinate);

        lock (_gate)
        {
            if (_activeDownloads >= limits.MaxConcurrentDownloads ||
                _activeByCoordinate.GetValueOrDefault(coordinate) >= limits.MaxConcurrentDownloadsPerCoordinate)
            {
                return null;
            }

            _activeDownloads++;
            _activeByCoordinate[coordinate] = _activeByCoordinate.GetValueOrDefault(coordinate) + 1;
            return new MirrorDownloadAdmissionLease(this, coordinate);
        }
    }

    private void Release(string coordinate)
    {
        lock (_gate)
        {
            _activeDownloads--;
            var coordinateActive = _activeByCoordinate[coordinate] - 1;
            if (coordinateActive == 0)
            {
                _activeByCoordinate.Remove(coordinate);
            }
            else
            {
                _activeByCoordinate[coordinate] = coordinateActive;
            }
        }
    }

    public sealed class MirrorDownloadAdmissionLease : IDisposable
    {
        private MirrorDownloadAdmission? _owner;
        private readonly string _coordinate;

        internal MirrorDownloadAdmissionLease(MirrorDownloadAdmission owner, string coordinate)
        {
            _owner = owner;
            _coordinate = coordinate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_coordinate);
        }
    }
}
