---
description: "Build a custom ICheckpointStore for Spectra using Redis, Postgres, Cosmos DB, or any durable backend."
---

# Build Your Own Checkpoint Store

Spectra ships with `InMemoryCheckpointStore` and `FileCheckpointStore` for development and testing.

For production, you will usually want a durable backend such as Redis, Postgres, Cosmos DB, or your own storage system.

This guide shows the contract, the storage model, and the behaviors your checkpoint store must support.

---

## When to build a custom checkpoint store

Build a custom checkpoint store when you need:

- durable workflow recovery
- resume after process restarts
- shared checkpoint storage across multiple app instances
- checkpoint history for debugging or audit
- time travel and forking in production

A simple rule:

- use built-in stores for local development
- build your own store for real production persistence

---

## Step 1 — Implement `ICheckpointStore`

A checkpoint store does more than save the latest state.

It also supports:

- full checkpoint history
- loading checkpoints by index
- forking a run
- tracing lineage back to parent runs

```csharp
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.State;

public class RedisCheckpointStore : ICheckpointStore
{
    // --- Core methods ---

    public Task SaveAsync(Checkpoint checkpoint, CancellationToken ct = default)
    {
        // Serialize with CheckpointSerializer.Serialize(checkpoint)
        // Append to a list keyed by checkpoint.RunId (to preserve history)
        // Set the Index to the position in the list
    }

    public Task<Checkpoint?> LoadAsync(string runId, CancellationToken ct = default)
    {
        // Return the last (most recent) checkpoint for this runId
    }

    public Task<Checkpoint?> LoadLatestAsync(string workflowId, CancellationToken ct = default)
    {
        // Query across all runs by workflowId, order by UpdatedAt descending, return first
    }

    public Task DeleteAsync(string runId, CancellationToken ct = default)
    {
        // Remove the latest checkpoint entry for runId
    }

    public Task<IReadOnlyList<Checkpoint>> ListAsync(
        string? workflowId = null, CancellationToken ct = default)
    {
        // Return all (or filtered) checkpoints, ordered by UpdatedAt descending
    }

    // --- Time travel methods ---

    public Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken ct = default)
    {
        // Load a specific checkpoint by its sequential index within a run
    }

    public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken ct = default)
    {
        // Return all checkpoints for a run, ordered by index ascending
    }

    // --- Fork methods ---

    public Task<Checkpoint> ForkAsync(
        string sourceRunId, int checkpointIndex, string newRunId,
        WorkflowState? stateOverrides = null, CancellationToken ct = default)
    {
        // Load checkpoint at sourceRunId/checkpointIndex
        // Deep-clone the state, apply overrides if provided
        // Create a new checkpoint under newRunId with ParentRunId and ParentCheckpointIndex set
        // Save and return the forked checkpoint
    }

    public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken ct = default)
    {
        // Walk ParentRunId chain from this run back to the root, return ancestor list
    }

    public Task PurgeAsync(string runId, CancellationToken ct = default)
    {
        // Remove ALL checkpoints for a run (entire history)
    }
}
```

---

## Step 2 — Understand what a checkpoint store must preserve

A checkpoint store is not just a key-value lookup for "latest run state".

It must preserve:

- the latest checkpoint for resume
- the ordered history of checkpoints in a run
- enough metadata for time travel and forking
- parent-child lineage between runs

A good mental model is:

- one **run**
- many **checkpoints**
- each checkpoint has an **index**
- forked runs point back to a **parent run**

---

## Step 3 — Design the storage model

A practical storage design usually looks like this:

| Concern | Recommendation |
| --- | --- |
| Latest checkpoint lookup | Index by `RunId` |
| Full history | Store checkpoints per run in index order |
| Workflow-level lookup | Index by `WorkflowId` and `UpdatedAt` |
| Time travel | Support load by `RunId + Index` |
| Fork lineage | Store `ParentRunId` and parent checkpoint index |
| Serialization | Store the full checkpoint as JSON or versioned binary |

The most important rule is:

**preserve history, do not overwrite it**

Checkpoint history is required for:

- resume
- debugging
- time travel
- forking
- lineage queries

---

## Step 4 — Implement the core methods

### `SaveAsync`

Save a checkpoint as the next entry in the run history.

```csharp
public Task SaveAsync(Checkpoint checkpoint, CancellationToken ct = default)
{
    // Append to run history
}
```

A good implementation should:

- append, not replace
- assign the correct sequential `Index`
- preserve `UpdatedAt`
- make the checkpoint visible to latest-run queries

### `LoadAsync`

Return the most recent checkpoint for a run.

```csharp
public Task<Checkpoint?> LoadAsync(string runId, CancellationToken ct = default)
{
    // Load latest checkpoint for run
}
```

This is the method the runner uses most often for resume-from-latest behavior.

### `LoadLatestAsync`

Return the newest checkpoint across runs for a workflow.

```csharp
public Task<Checkpoint?> LoadLatestAsync(string workflowId, CancellationToken ct = default)
{
    // Latest checkpoint for workflow
}
```

This is useful for workflow-level inspection or operational tools.

### `DeleteAsync`

Delete only the latest checkpoint entry for a run.

```csharp
public Task DeleteAsync(string runId, CancellationToken ct = default)
{
    // Remove latest checkpoint only
}
```

This is different from `PurgeAsync`, which deletes the full history.

### `ListAsync`

Return checkpoints, optionally filtered by workflow.

```csharp
public Task<IReadOnlyList<Checkpoint>> ListAsync(
    string? workflowId = null, CancellationToken ct = default)
{
    // List checkpoints ordered by UpdatedAt descending
}
```

---

## Step 5 — Support time travel

Time travel depends on the ability to load historical checkpoints, not just the latest one.

### `LoadByIndexAsync`

Load one specific checkpoint in a run.

```csharp
public Task<Checkpoint?> LoadByIndexAsync(string runId, int index, CancellationToken ct = default)
{
    // Load checkpoint by index
}
```

### `ListByRunAsync`

Return the ordered history for one run.

```csharp
public Task<IReadOnlyList<Checkpoint>> ListByRunAsync(string runId, CancellationToken ct = default)
{
    // Return all checkpoints in ascending index order
}
```

Use ascending index order so the run history reads naturally from start to latest checkpoint.

---

## Step 6 — Implement forking

Forking creates a new run from a historical checkpoint.

```csharp
public Task<Checkpoint> ForkAsync(
    string sourceRunId, int checkpointIndex, string newRunId,
    WorkflowState? stateOverrides = null, CancellationToken ct = default)
{
    // Create a new run from an old checkpoint
}
```

A correct fork implementation should:

1. load the source checkpoint
2. deep-clone its state
3. apply any overrides
4. assign the new run ID
5. set `ParentRunId`
6. save the new run's first checkpoint

The cloned state must be independent from the source run. Do not share mutable references across runs.

!!! warning
    Forking should never mutate the original run. Treat the source checkpoint as immutable history.

---

## Step 7 — Implement lineage

Lineage lets you understand where a run came from.

### `GetLineageAsync`

Walk back through parent runs until you reach the root.

```csharp
public Task<IReadOnlyList<Checkpoint>> GetLineageAsync(string runId, CancellationToken ct = default)
{
    // Walk ParentRunId chain back to the root
}
```

This is useful for:

- audit trails
- production debugging
- tracing sandbox runs back to real runs
- experimentation history

### `PurgeAsync`

Delete the entire history for one run.

```csharp
public Task PurgeAsync(string runId, CancellationToken ct = default)
{
    // Remove all checkpoints for the run
}
```

Use this when you want a hard cleanup of the run's checkpoint history.

---

## Step 8 — Serialize carefully

A checkpoint contains full workflow state, not just a few fields.

That means your store should serialize and deserialize consistently.

A practical approach is:

- serialize the full checkpoint
- preserve all fields
- preserve index ordering
- keep format evolution in mind if your store will survive upgrades

If you use JSON, treat the stored checkpoint as an opaque payload plus queryable metadata fields.

A good split is:

- metadata columns or keys for indexing
- full serialized checkpoint for full restore

---

## Step 9 — Register the store

Once implemented, register it with Spectra:

```csharp
services.AddSpectra(builder =>
{
    builder.AddCheckpoints(new RedisCheckpointStore());
});
```

You can also configure checkpoint behavior at registration time:

```csharp
services.AddSpectra(builder =>
{
    builder.AddCheckpoints(new RedisCheckpointStore(), options =>
    {
        options.Frequency = CheckpointFrequency.EveryNode;
        options.CheckpointOnFailure = true;
        options.CheckpointOnInterrupt = true;
        options.CheckpointOnAwaitingInput = true;
    });
});
```

After that, the runner uses your store for checkpoint save/load behavior automatically.

---

## Testing your store

At minimum, test these scenarios:

- saving then loading the latest checkpoint
- saving multiple checkpoints preserves index order
- loading by index returns the expected historical checkpoint
- listing by run returns the full ordered history
- forking creates a new run without mutating the source
- lineage returns the ancestor chain correctly
- purge deletes the full run history
- cancellation tokens are honored

Example test shape:

```csharp
[Fact]
public async Task SaveAsync_Then_LoadAsync_Returns_Latest_Checkpoint()
{
    var store = new RedisCheckpointStore();

    var checkpoint = new Checkpoint
    {
        RunId = "run-1",
        WorkflowId = "wf-1",
        Index = 0,
        UpdatedAt = DateTimeOffset.UtcNow,
        Status = CheckpointStatus.InProgress,
        State = new WorkflowState()
    };

    await store.SaveAsync(checkpoint);
    var loaded = await store.LoadAsync("run-1");

    Assert.NotNull(loaded);
    Assert.Equal("run-1", loaded!.RunId);
}
```

!!! tip
    Forking and lineage are the two easiest areas to get subtly wrong. Test them explicitly, not just the latest-checkpoint path.

---

## Storage design tips

A few defaults work well for most backends:

| Concern | Recommendation |
| --- | --- |
| Run partitioning | Group checkpoints by `RunId` |
| History order | Use monotonically increasing `Index` |
| Latest lookup | Cache or index the newest checkpoint per run |
| Workflow lookup | Index by `WorkflowId` and `UpdatedAt` |
| Fork traceability | Store `ParentRunId` and parent checkpoint index |
| Serialization | Keep full checkpoint payload intact |

---

## Quick reference

| Task | How |
| --- | --- |
| Build a checkpoint store | Implement `ICheckpointStore` |
| Save latest state | `SaveAsync(checkpoint)` |
| Load latest checkpoint | `LoadAsync(runId)` |
| Load latest by workflow | `LoadLatestAsync(workflowId)` |
| Browse history | `ListByRunAsync(runId)` |
| Load historical checkpoint | `LoadByIndexAsync(runId, index)` |
| Fork a run | `ForkAsync(sourceRunId, checkpointIndex, newRunId, overrides)` |
| Trace lineage | `GetLineageAsync(runId)` |
| Delete latest checkpoint | `DeleteAsync(runId)` |
| Delete full run history | `PurgeAsync(runId)` |
| Register in Spectra | `builder.AddCheckpoints(new YourStore())` |

---

## A simple mental model

A checkpoint store is a durable execution history.

It must answer:

- where was this run last?
- what did this run look like at checkpoint N?
- can I resume from there?
- can I fork from there?
- where did this forked run come from?

If your store can answer those reliably, it is a good Spectra checkpoint backend.