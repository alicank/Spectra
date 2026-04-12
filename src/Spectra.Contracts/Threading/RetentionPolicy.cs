namespace Spectra.Contracts.Threading;

/// <summary>
/// Defines retention rules applied to threads and their checkpoints.
/// All thresholds are optional; only non-null values are enforced.
/// </summary>
public sealed class RetentionPolicy
{
    /// <summary>
    /// Delete threads older than this duration.
    /// Measured from <see cref="Thread.UpdatedAt"/>.
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Maximum number of checkpoints to retain per thread.
    /// When exceeded, the oldest checkpoints are removed.
    /// </summary>
    public int? MaxCheckpointsPerThread { get; set; }

    /// <summary>
    /// Only apply retention to threads with the given status.
    /// Null means apply to all statuses.
    /// </summary>
    public Checkpointing.CheckpointStatus? ApplyToStatus { get; set; }
}