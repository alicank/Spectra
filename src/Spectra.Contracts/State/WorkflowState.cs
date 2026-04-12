namespace Spectra.Contracts.State;

public class WorkflowState
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public string WorkflowId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CurrentNodeId { get; set; } = string.Empty;

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
    /// Accumulated error messages from failed steps.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}
