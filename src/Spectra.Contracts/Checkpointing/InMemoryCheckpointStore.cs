using System.Collections.Concurrent;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;

namespace Spectra.Extensions.Checkpointing;

public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, List<Checkpoint>> _checkpointsByRun = new();
    private readonly object _lock = new();

    public Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var list = _checkpointsByRun.GetOrAdd(checkpoint.RunId, _ => new List<Checkpoint>());
            var indexed = checkpoint with { Index = list.Count };
            list.Add(indexed);
        }

        return Task.CompletedTask;
    }

    public Task<Checkpoint?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_checkpointsByRun.TryGetValue(runId, out var list) && list.Count > 0)
                return Task.FromResult<Checkpoint?>(list[^1]);
        }

        return Task.FromResult<Checkpoint?>(null);
    }

    public Task<Checkpoint?> LoadLatestAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var latest = _checkpointsByRun.Values
                .SelectMany(l => l)
                .Where(c => c.WorkflowId == workflowId)
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefault();

            return Task.FromResult(latest);
        }
    }

    public Task DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _checkpointsByRun.TryRemove(runId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Checkpoint>> ListAsync(
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var results = _checkpointsByRun.Values
                .SelectMany(l => l)
                .Where(c => workflowId is null || c.WorkflowId == workflowId)
                .OrderByDescending(c => c.UpdatedAt)
                .ToList();

            return Task.FromResult<IReadOnlyList<Checkpoint>>(results);
        }
    }

    public Task<Checkpoint?> LoadByIndexAsync(
        string runId,
        int index,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_checkpointsByRun.TryGetValue(runId, out var list)
                && index >= 0 && index < list.Count)
            {
                return Task.FromResult<Checkpoint?>(list[index]);
            }
        }

        return Task.FromResult<Checkpoint?>(null);
    }

    public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_checkpointsByRun.TryGetValue(runId, out var list))
                return Task.FromResult<IReadOnlyList<Checkpoint>>(list.ToList());
        }

        return Task.FromResult<IReadOnlyList<Checkpoint>>(Array.Empty<Checkpoint>());
    }

    public Task<Checkpoint> ForkAsync(
        string sourceRunId,
        int checkpointIndex,
        string newRunId,
        WorkflowState? stateOverrides = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_checkpointsByRun.TryGetValue(sourceRunId, out var sourceList)
                || checkpointIndex < 0 || checkpointIndex >= sourceList.Count)
            {
                throw new InvalidOperationException(
                    $"Checkpoint index {checkpointIndex} not found for run '{sourceRunId}'.");
            }

            var source = sourceList[checkpointIndex];

            // Deep-clone state via serialization to prevent mutation leaking
            var clonedStateJson = System.Text.Json.JsonSerializer.Serialize(source.State);
            var clonedState = System.Text.Json.JsonSerializer.Deserialize<WorkflowState>(clonedStateJson)!;

            // Apply overrides if provided
            if (stateOverrides != null)
            {
                foreach (var kvp in stateOverrides.Context)
                    clonedState.Context[kvp.Key] = kvp.Value;

                foreach (var kvp in stateOverrides.Inputs)
                    clonedState.Inputs[kvp.Key] = kvp.Value;

                foreach (var kvp in stateOverrides.Artifacts)
                    clonedState.Artifacts[kvp.Key] = kvp.Value;
            }

            clonedState.RunId = newRunId;

            var forked = new Checkpoint
            {
                RunId = newRunId,
                WorkflowId = source.WorkflowId,
                State = clonedState,
                LastCompletedNodeId = source.LastCompletedNodeId,
                NextNodeId = source.NextNodeId,
                StepsCompleted = source.StepsCompleted,
                SchemaVersion = source.SchemaVersion,
                Status = CheckpointStatus.InProgress,
                Index = 0,
                ParentRunId = sourceRunId,
                ParentCheckpointIndex = checkpointIndex,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var newList = new List<Checkpoint> { forked };
            _checkpointsByRun[newRunId] = newList;

            return Task.FromResult(forked);
        }
    }

    public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lineage = new List<Checkpoint>();

        lock (_lock)
        {
            var currentRunId = runId;

            while (currentRunId != null)
            {
                if (!_checkpointsByRun.TryGetValue(currentRunId, out var list) || list.Count == 0)
                    break;

                var first = list[0];
                lineage.Add(first);

                currentRunId = first.ParentRunId;
            }
        }

        lineage.Reverse();
        return Task.FromResult<IReadOnlyList<Checkpoint>>(lineage);
    }

    public Task PurgeAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _checkpointsByRun.TryRemove(runId, out _);
        return Task.CompletedTask;
    }
}