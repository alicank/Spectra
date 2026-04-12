using System.Text.Json;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;

namespace Spectra.Extensions.Checkpointing;

public sealed class FileCheckpointStore : ICheckpointStore
{
    private readonly string _directory;

    public FileCheckpointStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Checkpoint directory is required.", nameof(directory));

        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    // ── Core methods ────────────────────────────────────────────────

    public async Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runDir = GetRunDirectory(checkpoint.RunId);
        Directory.CreateDirectory(runDir);

        var existing = Directory.GetFiles(runDir, "*.json");
        var index = existing.Length;

        var indexed = checkpoint with { Index = index };
        var json = CheckpointSerializer.Serialize(indexed);
        var path = Path.Combine(runDir, $"{index:D6}.json");

        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<Checkpoint?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runDir = GetRunDirectory(runId);
        if (!Directory.Exists(runDir)) return null;

        var files = Directory.GetFiles(runDir, "*.json").OrderBy(f => f).ToArray();
        if (files.Length == 0) return null;

        var json = await File.ReadAllTextAsync(files[^1], cancellationToken);
        return CheckpointSerializer.Deserialize(json);
    }

    public async Task<Checkpoint?> LoadLatestAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(workflowId, cancellationToken);
        return all.FirstOrDefault();
    }

    public Task DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runDir = GetRunDirectory(runId);
        if (Directory.Exists(runDir))
        {
            // Delete only the latest checkpoint file
            var files = Directory.GetFiles(runDir, "*.json").OrderBy(f => f).ToArray();
            if (files.Length > 0)
                File.Delete(files[^1]);

            if (Directory.GetFiles(runDir).Length == 0)
                Directory.Delete(runDir);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Checkpoint>> ListAsync(
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checkpoints = new List<Checkpoint>();

        if (!Directory.Exists(_directory)) return checkpoints;

        foreach (var runDir in Directory.GetDirectories(_directory))
        {
            var files = Directory.GetFiles(runDir, "*.json");
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cp = await TryReadCheckpoint(file, cancellationToken);
                if (cp != null && (workflowId is null || cp.WorkflowId == workflowId))
                    checkpoints.Add(cp);
            }
        }

        return checkpoints.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    // ── Time travel methods ─────────────────────────────────────────

    public async Task<Checkpoint?> LoadByIndexAsync(
        string runId,
        int index,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetCheckpointPath(runId, index);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return CheckpointSerializer.Deserialize(json);
    }

    public async Task<IReadOnlyList<Checkpoint>> ListByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runDir = GetRunDirectory(runId);
        if (!Directory.Exists(runDir))
            return Array.Empty<Checkpoint>();

        var files = Directory.GetFiles(runDir, "*.json").OrderBy(f => f).ToArray();
        var results = new List<Checkpoint>();

        foreach (var file in files)
        {
            var cp = await TryReadCheckpoint(file, cancellationToken);
            if (cp != null) results.Add(cp);
        }

        return results;
    }

    // ── Fork methods ────────────────────────────────────────────────

    public async Task<Checkpoint> ForkAsync(
        string sourceRunId,
        int checkpointIndex,
        string newRunId,
        WorkflowState? stateOverrides = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = await LoadByIndexAsync(sourceRunId, checkpointIndex, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Checkpoint index {checkpointIndex} not found for run '{sourceRunId}'.");

        // Deep-clone state via serialization
        var clonedStateJson = JsonSerializer.Serialize(source.State);
        var clonedState = JsonSerializer.Deserialize<WorkflowState>(clonedStateJson)!;

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

        // Save as the first checkpoint of the new run
        var newRunDir = GetRunDirectory(newRunId);
        Directory.CreateDirectory(newRunDir);
        var json = CheckpointSerializer.Serialize(forked);
        await File.WriteAllTextAsync(
            Path.Combine(newRunDir, "000000.json"), json, cancellationToken);

        return forked;
    }

    public async Task<IReadOnlyList<Checkpoint>> GetLineageAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lineage = new List<Checkpoint>();
        var currentRunId = runId;

        while (currentRunId != null)
        {
            var history = await ListByRunAsync(currentRunId, cancellationToken);
            if (history.Count == 0) break;

            var first = history[0];
            lineage.Add(first);
            currentRunId = first.ParentRunId;
        }

        lineage.Reverse();
        return lineage;
    }

    public Task PurgeAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runDir = GetRunDirectory(runId);
        if (Directory.Exists(runDir))
            Directory.Delete(runDir, recursive: true);

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private string GetRunDirectory(string runId) =>
        Path.Combine(_directory, runId);

    private string GetCheckpointPath(string runId, int index) =>
        Path.Combine(_directory, runId, $"{index:D6}.json");

    private static async Task<Checkpoint?> TryReadCheckpoint(
        string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return CheckpointSerializer.TryDeserialize(json);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}