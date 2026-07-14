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
    private readonly Counter<long> _publicationAttempts;
    private readonly Counter<long> _publicationConflicts;
    private readonly Counter<long> _authenticationDecisions;
    private readonly UpDownCounter<long> _mirrorActiveDownloads;
    private readonly Counter<long> _mirrorAdmissionRejections;
    private readonly Counter<long> _mirrorEvictions;

    public OperationalMetrics()
    {
        _outboxFailures = _meter.CreateCounter<long>("terraform_registry.outbox.failures");
        _outboxRetries = _meter.CreateCounter<long>("terraform_registry.outbox.retries");
        _outboxAgeMilliseconds = _meter.CreateHistogram<long>("terraform_registry.outbox.age_ms");
        _extractionClaimMilliseconds = _meter.CreateHistogram<long>("terraform_registry.extraction.claim_latency_ms");
        _extractionAttempts = _meter.CreateCounter<long>("terraform_registry.extraction.attempts");
        _extractionFailures = _meter.CreateCounter<long>("terraform_registry.extraction.failures");
        _publicationAttempts = _meter.CreateCounter<long>("terraform_registry.publication.attempts");
        _publicationConflicts = _meter.CreateCounter<long>("terraform_registry.publication.conflicts");
        _authenticationDecisions = _meter.CreateCounter<long>("terraform_registry.authentication.decisions");
        _mirrorActiveDownloads = _meter.CreateUpDownCounter<long>("terraform_registry.mirror.active_downloads");
        _mirrorAdmissionRejections = _meter.CreateCounter<long>("terraform_registry.mirror.admission_rejections");
        _mirrorEvictions = _meter.CreateCounter<long>("terraform_registry.mirror.evictions");
    }

    public void RecordOutboxClaim(DateTime createdAt) =>
        _outboxAgeMilliseconds.Record(Math.Max(0, (long)(DateTime.UtcNow - createdAt).TotalMilliseconds));
    public void RecordOutboxFailure(string outcome) => _outboxFailures.Add(1, Outcome(outcome));
    public void RecordOutboxRetry(string outcome) => _outboxRetries.Add(1, Outcome(outcome));
    public void RecordExtractionClaim(DateTime createdAt) =>
        _extractionClaimMilliseconds.Record(Math.Max(0, (long)(DateTime.UtcNow - createdAt).TotalMilliseconds));
    public void RecordExtractionAttempt() => _extractionAttempts.Add(1);
    public void RecordExtractionFailure(string outcome) => _extractionFailures.Add(1, Outcome(outcome));
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
    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> Outcome(string value) => new("outcome", value);
}
