namespace Spectra.Contracts.State;

/// <summary>
/// Describes the outcome of a workflow run. Carried on <see cref="WorkflowState.Status"/>
/// so callers can inspect the result without touching the checkpoint store.
/// </summary>
/// <remarks>
/// This enum mirrors <see cref="Spectra.Contracts.Checkpointing.CheckpointStatus"/> by design —
/// they are different names for the same state because checkpointing is one way to persist
/// that state. Keeping them structurally separate means <see cref="WorkflowState"/> does not
/// need to take a dependency on the checkpointing contracts.
/// </remarks>
public enum WorkflowRunStatus
{
    /// <summary>The run has not started or is mid-execution.</summary>
    InProgress,

    /// <summary>The run reached a terminal node without errors.</summary>
    Completed,

    /// <summary>The run stopped because of a step error or an interrupt rejection / timeout.</summary>
    Failed,

    /// <summary>The run is paused waiting for an interrupt response.</summary>
    Interrupted,

    /// <summary>The run is paused waiting for user input to a session node.</summary>
    AwaitingInput,

    /// <summary>
    /// The run was cleanly cancelled by an operator — typically via an
    /// Interrupt response with status Cancelled. Distinct from <see cref="Failed"/>:
    /// cancellation is an intentional stop, not an error.
    /// </summary>
    Cancelled
}