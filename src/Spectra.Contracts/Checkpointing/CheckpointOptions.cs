namespace Spectra.Contracts.Checkpointing;

public sealed class CheckpointOptions
{
    /// <summary>
    /// Controls when checkpoints are saved during workflow execution.
    /// </summary>
    public CheckpointFrequency Frequency { get; set; } = CheckpointFrequency.EveryNode;

    /// <summary>
    /// Maximum number of checkpoints to retain per workflow run.
    /// When exceeded, the oldest checkpoints are removed.
    /// Set to null for unlimited retention.
    /// </summary>
    public int? MaxCheckpointCount { get; set; }

    /// <summary>
    /// Time-to-live for completed checkpoints.
    /// Null means no automatic expiration.
    /// </summary>
    public TimeSpan? RetentionPeriod { get; set; }

    /// <summary>
    /// Whether to save a checkpoint when a step fails.
    /// Default is true so that failures can be inspected.
    /// </summary>
    public bool CheckpointOnFailure { get; set; } = true;

    /// <summary>
    /// Whether to save a checkpoint when a step needs continuation (interrupt / human gate).
    /// Default is true so execution can resume later.
    /// </summary>
    public bool CheckpointOnContinuation { get; set; } = true;

    /// <summary>
    /// Whether to save a checkpoint when a step is interrupted.
    /// Default is true so execution can resume with the interrupt response.
    /// </summary>
    public bool CheckpointOnInterrupt { get; set; } = true;

    /// <summary>
    /// Whether to save a checkpoint when a session step is awaiting user input.
    /// Default is true so conversations can survive process restarts.
    /// </summary>
    public bool CheckpointOnAwaitingInput { get; set; } = true;

    public static CheckpointOptions Default => new();
}

public enum CheckpointFrequency
{
    /// <summary>Save a checkpoint after every node execution.</summary>
    EveryNode,

    /// <summary>Save a checkpoint only when the workflow status changes (e.g., InProgress → Failed).</summary>
    StatusChangeOnly,

    /// <summary>Never save checkpoints automatically. Manual save only.</summary>
    Disabled
}