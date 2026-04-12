# Checkpointing & Resume

Spectra workflows can be paused at any point and resumed later. This is essential for workflows that include human approval gates, long-running external calls, or need crash recovery.

## How It Works

A **checkpoint** captures the complete workflow state at a specific node:

```csharp
public class Checkpoint
{
    public string WorkflowName { get; }
    public string RunId { get; }
    public string NodeId { get; }              // Where execution paused
    public WorkflowState State { get; }        // Full state snapshot
    public DateTime CreatedAt { get; }
}
```

When a step returns `StepStatus.WaitingForHuman` or `StepStatus.Interrupted`, the runner automatically saves a checkpoint and stops execution.

## Enabling Checkpoints

```csharp
var result = await runner.RunAsync(workflow, inputs, new CheckpointOptions
{
    Store = new FileCheckpointStore("./checkpoints"),
    SaveAfterEachStep = true  // or only on interrupts
});
```

## Resuming a Workflow

```csharp
var checkpoint = await store.LoadAsync(runId);
var result = await runner.ResumeAsync(checkpoint, additionalInputs);
```

The runner picks up from the node where execution stopped, with the full state restored.

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
    Task<IReadOnlyList<Checkpoint>> ListAsync(string workflowName, CancellationToken ct = default);
    Task DeleteAsync(string runId, CancellationToken ct = default);
}
```

See [Build Your Own Checkpoint Store](../guides/custom-checkpoint-store.md) for a complete implementation guide.

## Time Travel (Fork from Checkpoint)

You can load any historical checkpoint and fork a new execution from that point:

```csharp
var checkpoints = await store.ListAsync("my-workflow");
var oldCheckpoint = checkpoints.First(c => c.NodeId == "analyze");

// Fork: resume from "analyze" with different inputs
var forkedResult = await runner.ResumeAsync(oldCheckpoint, new Dictionary<string, object>
{
    ["threshold"] = 0.9  // try a different threshold
});
```

This creates a new run that diverges from the original execution, useful for experimentation and debugging.
