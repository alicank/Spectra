using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Steps;

namespace Spectra.Contracts.Events;

public sealed record WorkflowStartedEvent : WorkflowEvent
{
    public string? WorkflowName { get; init; }
    public int TotalNodes { get; init; }
}

public sealed record WorkflowCompletedEvent : WorkflowEvent
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public int StepsExecuted { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record WorkflowResumedEvent : WorkflowEvent
{
    public required string ResumedFromNodeId { get; init; }
    public int StepsAlreadyCompleted { get; init; }
}

public sealed record StepStartedEvent : WorkflowEvent
{
    public required string StepType { get; init; }
    public IReadOnlyDictionary<string, object?> Inputs { get; init; } =
        new Dictionary<string, object?>();
}

public sealed record StepCompletedEvent : WorkflowEvent
{
    public required string StepType { get; init; }
    public required StepStatus Status { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } =
        new Dictionary<string, object?>();
    public string? ErrorMessage { get; init; }
}

public sealed record StateChangedEvent : WorkflowEvent
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public object? Value { get; init; }
}

public sealed record BranchEvaluatedEvent : WorkflowEvent
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required string Condition { get; init; }
    public bool Result { get; init; }
    public string? Reason { get; init; }
}

public sealed record ParallelBatchStartedEvent : WorkflowEvent
{
    public required IReadOnlyList<string> NodeIds { get; init; }
    public int BatchSize { get; init; }
}

public sealed record ParallelBatchCompletedEvent : WorkflowEvent
{
    public required IReadOnlyList<string> NodeIds { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed record StepInterruptedEvent : WorkflowEvent
{
    public required string StepType { get; init; }
    public required string Reason { get; init; }
    public string? InterruptTitle { get; init; }
    public bool IsDeclarative { get; init; }
}

public sealed record TokenStreamEvent : WorkflowEvent
{
    /// <summary>The text delta (partial token / chunk) from the LLM provider.</summary>
    public required string Token { get; init; }

    /// <summary>Zero-based index of this token within the current node's stream.</summary>
    public int TokenIndex { get; init; }

    /// <summary>When true, this is the final token for the current node stream.</summary>
    public bool IsComplete { get; init; }
}