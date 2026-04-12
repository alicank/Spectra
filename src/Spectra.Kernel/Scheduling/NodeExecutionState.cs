namespace Spectra.Kernel.Scheduling;

public enum NodeExecutionStatus
{
    Pending,
    Ready,
    Running,
    Completed,
    Failed,
    Interrupted,
    Skipped
}

public class NodeExecutionState
{
    public required string NodeId { get; init; }
    public NodeExecutionStatus Status { get; set; } = NodeExecutionStatus.Pending;
    public HashSet<string> CompletedDependencies { get; } = [];
    public int TotalDependencies { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int IterationCount { get; set; } = 0;
}