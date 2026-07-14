using System.Diagnostics.Metrics;

namespace TerraformRegistry.Services;

/// <summary>Low-cardinality operational measurements for durable registry workflows.</summary>
public sealed class OperationalMetrics : IDisposable
{
    public const string MeterName = "TerraformRegistry.Operations";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _outboxFailures;
    private readonly Counter<long> _outboxRetries;
    private readonly Histogram<long> _outboxAgeMilliseconds;
    private readonly Histogram<long> _extractionClaimMilliseconds;
    private readonly Counter<long> _extractionAttempts;
    private readonly Counter<long> _extractionFailures;
    private readonly ObservableGauge<long> _extractionQueueDepth;
    private readonly Counter<long> _publicationAttempts;
    private readonly Counter<long> _publicationConflicts;
    private readonly Counter<long> _authenticationDecisions;
    private readonly UpDownCounter<long> _mirrorActiveDownloads;
    private readonly Counter<long> _mirrorAdmissionRejections;
    private readonly Counter<long> _mirrorEvictions;
    private readonly Counter<long> _mirrorUpstreamRequests;
    private readonly Counter<long> _mirrorNegativeCacheHits;
    private readonly Counter<long> _mirrorLeaseLosses;
    private readonly ObservableGauge<long> _mirrorCacheBytes;
    private long _extractionQueueDepthValue;
    private long _mirrorCacheBytesValue;

    public OperationalMetrics()
    {
        _outboxFailures = _meter.CreateCounter<long>("terraform_registry.outbox.failures");
        _outboxRetries = _meter.CreateCounter<long>("terraform_registry.outbox.retries");
        _outboxAgeMilliseconds = _meter.CreateHistogram<long>("terraform_registry.outbox.age_ms");
        _extractionClaimMilliseconds = _meter.CreateHistogram<long>("terraform_registry.extraction.claim_latency_ms");
        _extractionAttempts = _meter.CreateCounter<long>("terraform_registry.extraction.attempts");
        _extractionFailures = _meter.CreateCounter<long>("terraform_registry.extraction.failures");
        _extractionQueueDepth = _meter.CreateObservableGauge<long>("terraform_registry.extraction.queue_depth",
            () => Volatile.Read(ref _extractionQueueDepthValue));
        _publicationAttempts = _meter.CreateCounter<long>("terraform_registry.publication.attempts");
        _publicationConflicts = _meter.CreateCounter<long>("terraform_registry.publication.conflicts");
        _authenticationDecisions = _meter.CreateCounter<long>("terraform_registry.authentication.decisions");
        _mirrorActiveDownloads = _meter.CreateUpDownCounter<long>("terraform_registry.mirror.active_downloads");
        _mirrorAdmissionRejections = _meter.CreateCounter<long>("terraform_registry.mirror.admission_rejections");
        _mirrorEvictions = _meter.CreateCounter<long>("terraform_registry.mirror.evictions");
        _mirrorUpstreamRequests = _meter.CreateCounter<long>("terraform_registry.mirror.upstream_requests");
        _mirrorNegativeCacheHits = _meter.CreateCounter<long>("terraform_registry.mirror.negative_cache_hits");
        _mirrorLeaseLosses = _meter.CreateCounter<long>("terraform_registry.mirror.lease_losses");
        _mirrorCacheBytes = _meter.CreateObservableGauge<long>("terraform_registry.mirror.cache_bytes",
            () => Volatile.Read(ref _mirrorCacheBytesValue));
    }

    public void RecordOutboxClaim(DateTime createdAt) =>
        _outboxAgeMilliseconds.Record(Math.Max(0, (long)(DateTime.UtcNow - createdAt).TotalMilliseconds));
    public void RecordOutboxFailure(string outcome) => _outboxFailures.Add(1, Outcome(outcome));
    public void RecordOutboxRetry(string outcome) => _outboxRetries.Add(1, Outcome(outcome));
    public void RecordExtractionClaim(DateTime createdAt) =>
        _extractionClaimMilliseconds.Record(Math.Max(0, (long)(DateTime.UtcNow - createdAt).TotalMilliseconds));
    public void RecordExtractionAttempt() => _extractionAttempts.Add(1);
    public void RecordExtractionFailure(string outcome) => _extractionFailures.Add(1, Outcome(outcome));
    public void RecordExtractionQueueDepth(int depth) => Interlocked.Exchange(ref _extractionQueueDepthValue, Math.Max(0, depth));
    public void RecordPublicationAttempt() => _publicationAttempts.Add(1);
    public void RecordPublicationConflict() => _publicationConflicts.Add(1);
    public void RecordAuthenticationDecision(string decision) => _authenticationDecisions.Add(1, Outcome(decision));
    public void RecordMirrorAdmission(bool admitted)
    {
        if (admitted) _mirrorActiveDownloads.Add(1);
        else _mirrorAdmissionRejections.Add(1);
    }
    public void RecordMirrorRelease() => _mirrorActiveDownloads.Add(-1);
    public void RecordMirrorEviction(string kind) => _mirrorEvictions.Add(1, Outcome(kind));
    public void RecordMirrorUpstreamRequest(string kind) => _mirrorUpstreamRequests.Add(1, Kind(kind));
    public void RecordMirrorNegativeCacheHit(string kind) => _mirrorNegativeCacheHits.Add(1, Kind(kind));
    public void RecordMirrorLeaseLoss(string kind) => _mirrorLeaseLosses.Add(1, Kind(kind));
    public void RecordMirrorCacheBytes(long bytes) => Interlocked.Exchange(ref _mirrorCacheBytesValue, Math.Max(0, bytes));
    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> Outcome(string value) => new("outcome", value);
    private static KeyValuePair<string, object?> Kind(string value) => new("kind", value);
}
