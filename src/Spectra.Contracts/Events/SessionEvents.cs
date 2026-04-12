namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted when a session node processes a user turn and produces a response.
/// </summary>
public record SessionTurnCompletedEvent : WorkflowEvent
{
    public required int TurnNumber { get; init; }
    public required string AssistantResponse { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int ToolCallCount { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Emitted when a session node suspends and awaits the next user message.
/// </summary>
public record SessionAwaitingInputEvent : WorkflowEvent
{
    public required int TurnsCompleted { get; init; }
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
}

/// <summary>
/// Emitted when a session node completes (exits) due to an exit policy being satisfied.
/// </summary>
public record SessionCompletedEvent : WorkflowEvent
{
    public required int TotalTurns { get; init; }
    public required string ExitReason { get; init; }
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
}