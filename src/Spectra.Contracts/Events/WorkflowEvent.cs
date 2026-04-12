namespace Spectra.Contracts.Events;

public abstract record WorkflowEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public string? NodeId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string EventType { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
}