using System.Collections.Concurrent;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Threading;

namespace Spectra.Kernel.Threading;

/// <summary>
/// In-memory implementation of <see cref="IThreadManager"/> for development,
/// testing, and single-process deployments.
/// </summary>
public sealed class InMemoryThreadManager : IThreadManager
{
    private readonly ConcurrentDictionary<string, Contracts.Threading.Thread> _threads = new();
    private readonly ICheckpointStore? _checkpointStore;
    private readonly object _lock = new();

    public InMemoryThreadManager(ICheckpointStore? checkpointStore = null)
    {
        _checkpointStore = checkpointStore;
    }

    public Task<Contracts.Threading.Thread> CreateAsync(
        Contracts.Threading.Thread thread,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(thread);

        if (!_threads.TryAdd(thread.ThreadId, thread))
            throw new InvalidOperationException($"Thread '{thread.ThreadId}' already exists.");

        return Task.FromResult(thread);
    }

    public Task<Contracts.Threading.Thread?> GetAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _threads.TryGetValue(threadId, out var thread);
        return Task.FromResult(thread);
    }

    public Task<Contracts.Threading.Thread> UpdateAsync(
        Contracts.Threading.Thread thread,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(thread);

        if (!_threads.ContainsKey(thread.ThreadId))
            throw new InvalidOperationException($"Thread '{thread.ThreadId}' not found.");

        var updated = thread with { UpdatedAt = DateTimeOffset.UtcNow };
        _threads[thread.ThreadId] = updated;
        return Task.FromResult(updated);
    }

    public async Task DeleteAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_threads.TryRemove(threadId, out var thread) && _checkpointStore != null)
        {
            await _checkpointStore.PurgeAsync(thread.RunId, cancellationToken);
        }
    }

    public Task<IReadOnlyList<Contracts.Threading.Thread>> ListAsync(
        ThreadFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = _threads.Values.AsEnumerable();
        query = ApplyFilter(query, filter);

        var results = query
            .OrderByDescending(t => t.UpdatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<Contracts.Threading.Thread>>(results);
    }

    public async Task<Contracts.Threading.Thread> CloneAsync(
        string sourceThreadId,
        string? newThreadId = null,
        bool cloneCheckpoints = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_threads.TryGetValue(sourceThreadId, out var source))
            throw new InvalidOperationException($"Thread '{sourceThreadId}' not found.");

        var clonedThreadId = newThreadId ?? Guid.NewGuid().ToString();
        var clonedRunId = Guid.NewGuid().ToString();

        // Clone checkpoints if requested and a checkpoint store is available
        if (cloneCheckpoints && _checkpointStore != null)
        {
            var checkpoints = await _checkpointStore.ListByRunAsync(source.RunId, cancellationToken);
            if (checkpoints.Count > 0)
            {
                // Fork from the latest checkpoint
                var latestIndex = checkpoints[^1].Index;
                await _checkpointStore.ForkAsync(
                    source.RunId, latestIndex, clonedRunId, cancellationToken: cancellationToken);
            }
        }

        var cloned = source with
        {
            ThreadId = clonedThreadId,
            RunId = clonedRunId,
            SourceThreadId = sourceThreadId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(source.Metadata)
        };

        if (!_threads.TryAdd(clonedThreadId, cloned))
            throw new InvalidOperationException($"Thread '{clonedThreadId}' already exists.");

        return cloned;
    }

    public async Task<RetentionResult> ApplyRetentionPolicyAsync(
        RetentionPolicy policy,
        ThreadFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(policy);

        var threadsDeleted = 0;
        var checkpointsTrimmed = 0;

        var candidates = ApplyFilter(_threads.Values, filter).ToList();

        foreach (var thread in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check age-based retention
            if (policy.MaxAge.HasValue)
            {
                var age = DateTimeOffset.UtcNow - thread.UpdatedAt;
                if (age > policy.MaxAge.Value)
                {
                    // Check status filter if present
                    if (policy.ApplyToStatus.HasValue && _checkpointStore != null)
                    {
                        var latest = await _checkpointStore.LoadAsync(thread.RunId, cancellationToken);
                        if (latest != null && latest.Status != policy.ApplyToStatus.Value)
                            continue;
                    }

                    await DeleteAsync(thread.ThreadId, cancellationToken);
                    threadsDeleted++;
                    continue;
                }
            }

            // Check checkpoint count retention
            if (policy.MaxCheckpointsPerThread.HasValue && _checkpointStore != null)
            {
                var checkpoints = await _checkpointStore.ListByRunAsync(thread.RunId, cancellationToken);
                if (checkpoints.Count > policy.MaxCheckpointsPerThread.Value)
                {
                    var toRemove = checkpoints.Count - policy.MaxCheckpointsPerThread.Value;
                    // Purge and re-save only the ones we want to keep.
                    // For in-memory, we do a full purge and re-save the latest N.
                    var toKeep = checkpoints
                        .OrderByDescending(c => c.Index)
                        .Take(policy.MaxCheckpointsPerThread.Value)
                        .OrderBy(c => c.Index)
                        .ToList();

                    await _checkpointStore.PurgeAsync(thread.RunId, cancellationToken);

                    foreach (var cp in toKeep)
                    {
                        await _checkpointStore.SaveAsync(
                            cp with { Index = 0 }, // Index re-assigned by SaveAsync
                            cancellationToken);
                    }

                    checkpointsTrimmed += toRemove;
                }
            }
        }

        return new RetentionResult
        {
            ThreadsDeleted = threadsDeleted,
            CheckpointsTrimmed = checkpointsTrimmed
        };
    }

    public async Task<int> BulkDeleteAsync(
        ThreadFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);

        var candidates = ApplyFilter(_threads.Values, filter).ToList();
        var count = 0;

        foreach (var thread in candidates)
        {
            await DeleteAsync(thread.ThreadId, cancellationToken);
            count++;
        }

        return count;
    }

    private static IEnumerable<Contracts.Threading.Thread> ApplyFilter(
        IEnumerable<Contracts.Threading.Thread> threads,
        ThreadFilter? filter)
    {
        if (filter is null) return threads;

        var query = threads;

        if (filter.TenantId is not null)
            query = query.Where(t => t.TenantId == filter.TenantId);

        if (filter.UserId is not null)
            query = query.Where(t => t.UserId == filter.UserId);

        if (filter.WorkflowId is not null)
            query = query.Where(t => t.WorkflowId == filter.WorkflowId);

        if (filter.CreatedBefore.HasValue)
            query = query.Where(t => t.CreatedAt < filter.CreatedBefore.Value);

        if (filter.CreatedAfter.HasValue)
            query = query.Where(t => t.CreatedAt > filter.CreatedAfter.Value);

        if (filter.Tags is { Count: > 0 })
            query = query.Where(t => filter.Tags.All(tag => t.Tags.Contains(tag)));

        if (filter.LabelContains is not null)
            query = query.Where(t =>
                t.Label != null &&
                t.Label.Contains(filter.LabelContains, StringComparison.OrdinalIgnoreCase));

        return query;
    }
}