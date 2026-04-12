namespace Spectra.Contracts.Events;

public sealed record WorkflowForkedEvent : WorkflowEvent
{
    public required string SourceRunId { get; init; }

    public required int SourceCheckpointIndex { get; init; }
}