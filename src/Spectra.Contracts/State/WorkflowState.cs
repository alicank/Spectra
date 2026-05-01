namespace Spectra.Contracts.State;

public class WorkflowState
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public string WorkflowId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CurrentNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Terminal or current status of this run. Set by the runner throughout execution
    /// and finalized before <see cref="Execution.IWorkflowRunner.RunAsync(Workflow.WorkflowDefinition, WorkflowState?, CancellationToken)"/>
    /// returns. Consumers can inspect this to distinguish Completed / Failed / Cancelled / Interrupted
    /// without loading the checkpoint.
    /// </summary>
    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.InProgress;

    /// <summary>
    /// Initial workflow inputs provided at launch time.
    /// </summary>
    public Dictionary<string, object?> Inputs { get; set; } = [];

    /// <summary>
    /// Shared mutable state populated by steps during execution.
    /// Extension concerns (e.g. agent memory, cost tracking) can be stored here.
    /// </summary>
    public Dictionary<string, object?> Context { get; set; } = [];

    /// <summary>
    /// Named artifacts produced by the workflow (files, results, etc.).
    /// </summary>
    public Dictionary<string, object?> Artifacts { get; set; } = [];

    /// <summary>
    /// Node outputs keyed by node ID. Populated unconditionally after each node executes,
    /// enabling <c>{{nodes.nodeId.field}}</c> template resolution regardless of output mappings.
    /// </summary>
    public Dictionary<string, object?> Nodes { get; set; } = [];

    /// <summary>
    /// Accumulated error messages from failed steps.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}