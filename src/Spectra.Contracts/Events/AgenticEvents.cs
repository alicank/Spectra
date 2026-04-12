namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted at the end of each iteration of an agentic loop,
/// after all tool calls for that cycle have been executed.
/// </summary>
public sealed record AgentIterationEvent : WorkflowEvent
{
    public required int Iteration { get; init; }
    public required int ToolCallCount { get; init; }
    public required List<string> ToolNames { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
}

/// <summary>
/// Emitted for each individual tool execution within an agentic loop iteration.
/// </summary>
public sealed record AgentToolCallEvent : WorkflowEvent
{
    public required int Iteration { get; init; }
    public required string ToolName { get; init; }
    public required string ToolCallId { get; init; }
    public Dictionary<string, object?> Arguments { get; init; } = [];
    public bool ToolSuccess { get; init; }
    public string? ToolResultContent { get; init; }
    public string? ToolError { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Emitted when the agentic loop finishes — either the LLM produced a final
/// response without tool calls, or a guard limit was reached.
/// </summary>
public sealed record AgentCompletedEvent : WorkflowEvent
{
    public required int TotalIterations { get; init; }
    public required int TotalInputTokens { get; init; }
    public required int TotalOutputTokens { get; init; }
    public required string StopReason { get; init; }
}