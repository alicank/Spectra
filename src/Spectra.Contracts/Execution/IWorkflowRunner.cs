using Spectra.Contracts.Execution;
using Spectra.Contracts.Events;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.Execution;

/// <summary>
/// Abstraction for running workflows, enabling sub-workflow composition
/// without circular dependencies between Extensions and Kernel.
/// </summary>
public interface IWorkflowRunner
{
    Task<WorkflowState> RunAsync(
        WorkflowDefinition workflow,
        WorkflowState? initialState = null,
        CancellationToken cancellationToken = default);

    Task<WorkflowState> RunAsync(
        WorkflowDefinition workflow,
        WorkflowState? initialState,
        RunContext runContext,
        CancellationToken cancellationToken = default);

    Task<WorkflowState> ResumeAsync(
        WorkflowDefinition workflow,
        string runId,
        CancellationToken cancellationToken = default);

    Task<WorkflowState> ResumeFromCheckpointAsync(
        WorkflowDefinition workflow,
        string runId,
        int checkpointIndex,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// Resumes a workflow that was suspended by an interrupt, providing the response.
    /// </summary>
    Task<WorkflowState> ResumeWithResponseAsync(
        WorkflowDefinition workflow,
        string runId,
        Interrupts.InterruptResponse interruptResponse,
        CancellationToken cancellationToken = default);
    Task<WorkflowState> ForkAndRunAsync(
        WorkflowDefinition workflow,
        string sourceRunId,
        int checkpointIndex,
        string? newRunId = null,
        WorkflowState? stateOverrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a workflow and yields events (including token-level deltas) as they occur.
    /// </summary>
    IAsyncEnumerable<WorkflowEvent> StreamAsync(
        WorkflowDefinition workflow,
        StreamMode mode = StreamMode.Tokens,
        WorkflowState? initialState = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WorkflowEvent> StreamAsync(
        WorkflowDefinition workflow,
        StreamMode mode,
        WorkflowState? initialState,
        RunContext runContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message to a workflow that is awaiting input (e.g. a session step).
    /// Requires a checkpoint store to be configured.
    /// </summary>
    Task<WorkflowState> SendMessageAsync(
        WorkflowDefinition workflow,
        string runId,
        string userMessage,
        CancellationToken cancellationToken = default);
}