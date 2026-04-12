namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted when a tool's circuit breaker transitions between states
/// (closed → open, open → half-open, half-open → closed, half-open → open).
/// Flows through the event sink pipeline into audit trail and telemetry automatically.
/// </summary>
public sealed record ToolCircuitStateChangedEvent : WorkflowEvent
{
    public required string ToolName { get; init; }
    public required string PreviousState { get; init; }
    public required string NewState { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Emitted when a tool call is skipped because its circuit breaker is open.
/// Includes the fallback tool name if one was configured and used.
/// </summary>
public sealed record ToolCallSkippedEvent : WorkflowEvent
{
    public required string ToolName { get; init; }
    public required string CircuitState { get; init; }
    public string? FallbackToolName { get; init; }
    public bool FallbackUsed { get; init; }
    public string? Reason { get; init; }
}