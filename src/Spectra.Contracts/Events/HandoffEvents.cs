namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted when an agent initiates a handoff to another agent.
/// </summary>
public sealed record AgentHandoffEvent : WorkflowEvent
{
    public required string FromAgent { get; init; }
    public required string ToAgent { get; init; }
    public required string Intent { get; init; }
    public required int ChainDepth { get; init; }
    public required Workflow.ConversationScope ConversationScope { get; init; }
    public int TokensBudgetPassed { get; init; }
}

/// <summary>
/// Emitted when a supervisor delegates work to a worker agent.
/// </summary>
public sealed record AgentDelegationStartedEvent : WorkflowEvent
{
    public required string SupervisorAgent { get; init; }
    public required string WorkerAgent { get; init; }
    public required string Task { get; init; }
    public required int DelegationDepth { get; init; }
    public int BudgetAllocated { get; init; }
}

/// <summary>
/// Emitted when a delegated worker agent completes its work.
/// </summary>
public sealed record AgentDelegationCompletedEvent : WorkflowEvent
{
    public required string SupervisorAgent { get; init; }
    public required string WorkerAgent { get; init; }
    public required string Status { get; init; }
    public int TokensUsed { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ResultSummary { get; init; }
}

/// <summary>
/// Emitted when an agent escalates due to failure or budget exhaustion.
/// </summary>
public sealed record AgentEscalationEvent : WorkflowEvent
{
    public required string FailedAgent { get; init; }
    public required string EscalationTarget { get; init; }
    public required string Reason { get; init; }
    public string? FailureDetails { get; init; }
}

/// <summary>
/// Emitted when a handoff is blocked by a guard rail (cycle, depth, budget, policy).
/// </summary>
public sealed record AgentHandoffBlockedEvent : WorkflowEvent
{
    public required string FromAgent { get; init; }
    public required string ToAgent { get; init; }
    public required string Reason { get; init; }
}