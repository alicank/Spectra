# Checkpointing & Resume

Spectra workflows can be paused at any point and resumed later. This is essential for workflows that include human approval gates, long-running external calls, or need crash recovery.

## How It Works

A **checkpoint** captures the complete workflow state at a specific node:

```csharp
public sealed record Checkpoint
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required WorkflowState State { get; init; }

    public string? LastCompletedNodeId { get; init; }
    public string? NextNodeId { get; init; }
    public int StepsCompleted { get; init; }
    public int Index { get; init; }
    public CheckpointStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

When a step returns `StepStatus.NeedsContinuation`, `StepStatus.AwaitingInput`, or `StepStatus.Interrupted`, the runner saves a checkpoint and stops execution if the matching checkpoint option is enabled.

## Enabling Checkpoints

```csharp
services.AddSpectra(builder =>
{
    builder.AddFileCheckpoints("./checkpoints", opts =>
    {
        opts.Frequency = CheckpointFrequency.EveryNode;
        opts.CheckpointOnInterrupt = true;
        opts.CheckpointOnAwaitingInput = true;
        opts.CheckpointOnContinuation = true;
    });
});
```

## Resuming a Workflow

```csharp
var result = await runner.ResumeAsync(workflow, runId);
```

The runner restores the full state for that run and continues execution with that checkpoint context.

## Checkpoint Stores

Spectra provides two built-in stores:

**InMemoryCheckpointStore** — For testing and short-lived processes:

```csharp
var store = new InMemoryCheckpointStore();
```

**FileCheckpointStore** — Persists to disk as JSON files:

```csharp
var store = new FileCheckpointStore("./checkpoints");
```

For production, implement `ICheckpointStore` to persist to your database of choice:

```csharp
public interface ICheckpointStore
{
    Task SaveAsync(Checkpoint checkpoint, CancellationToken ct = default);
    Task<Checkpoint?> LoadAsync(string runId, CancellationToken ct = default);
    Task<Checkpoint?> LoadLatestAsync(string workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<Checkpoint>> ListAsync(string? workflowId = null, CancellationToken ct = default);
    Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken ct = default);
    Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken ct = default);
    Task<Checkpoint> ForkAsync(string sourceRunId, int checkpointIndex, string newRunId, WorkflowState? stateOverrides = null, CancellationToken ct = default);
    Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken ct = default);
    Task DeleteAsync(string runId, CancellationToken ct = default);
    Task PurgeAsync(string runId, CancellationToken ct = default);
}
```

See [Build Your Own Checkpoint Store](../guides/build-your-own-checkpoint-store.md) for a complete implementation guide.

## Time Travel (Fork from Checkpoint)

You can load any historical checkpoint and fork a new execution from that point:

```csharp
var checkpoints = await store.ListByRunAsync("run-abc-123");
var oldCheckpoint = checkpoints.First(c => c.LastCompletedNodeId == "analyze");

// Fork: resume from "analyze" with different inputs
var overrides = new WorkflowState();
overrides.Inputs["threshold"] = 0.9;  // try a different threshold

var forkedResult = await runner.ForkAndRunAsync(
    workflow,
    sourceRunId: "run-abc-123",
    checkpointIndex: oldCheckpoint.Index,
    stateOverrides: overrides);
```

This creates a new run that diverges from the original execution, useful for experimentation and debugging.
