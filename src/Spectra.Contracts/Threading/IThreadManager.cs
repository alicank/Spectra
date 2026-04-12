namespace Spectra.Contracts.Threading;

/// <summary>
/// First-class thread lifecycle manager. Treats conversations/threads as managed
/// entities with full CRUD, cloning, retention, and bulk cleanup.
/// </summary>
public interface IThreadManager
{
    /// <summary>Creates a new thread and returns it.</summary>
    Task<Thread> CreateAsync(
        Thread thread,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a thread by its ID. Returns null if not found.</summary>
    Task<Thread?> GetAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates mutable fields (Label, Tags, Metadata) of an existing thread.</summary>
    Task<Thread> UpdateAsync(
        Thread thread,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a thread and all of its associated checkpoints.</summary>
    Task DeleteAsync(
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists threads matching the optional filter, ordered by UpdatedAt descending.</summary>
    Task<IReadOnlyList<Thread>> ListAsync(
        ThreadFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deep-clones a thread: copies the Thread record and optionally
    /// all checkpoint history into a new thread.
    /// </summary>
    Task<Thread> CloneAsync(
        string sourceThreadId,
        string? newThreadId = null,
        bool cloneCheckpoints = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a retention policy to all threads matching the optional filter.
    /// Returns the number of threads and checkpoints affected.
    /// </summary>
    Task<RetentionResult> ApplyRetentionPolicyAsync(
        RetentionPolicy policy,
        ThreadFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-deletes all threads matching the filter.
    /// Returns the number of threads deleted.
    /// </summary>
    Task<int> BulkDeleteAsync(
        ThreadFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary of a retention policy application.
/// </summary>
public sealed record RetentionResult
{
    public int ThreadsDeleted { get; init; }
    public int CheckpointsTrimmed { get; init; }
}