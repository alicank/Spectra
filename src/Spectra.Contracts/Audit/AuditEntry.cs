namespace Spectra.Contracts.Audit;

/// <summary>
/// Immutable audit log entry capturing a single workflow event for compliance purposes.
/// Free tier stores entries without tamper-evident hashing; enterprise adds hash chains.
/// </summary>
public sealed record AuditEntry
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    // ── Identity context ──
    public string? TenantId { get; init; }
    public string? UserId { get; init; }

    // ── Workflow context ──
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public string? NodeId { get; init; }

    // ── Event data ──
    public required string EventType { get; init; }
    public string? EventData { get; init; }

    // ── Tracing correlation ──
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }

    /// <summary>
    /// Tamper-evident hash. Null in free tier; populated by enterprise audit store.
    /// </summary>
    public string? Checksum { get; init; }

    /// <summary>
    /// Hash of the previous entry in the chain. Null in free tier.
    /// </summary>
    public string? PreviousChecksum { get; init; }
}