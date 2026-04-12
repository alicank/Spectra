---
description: "Save and resume workflow execution with checkpoints in Spectra."
---

# Checkpointing

A **checkpoint** is a saved snapshot of workflow execution.

It lets Spectra resume from where a workflow stopped instead of starting again from the beginning.

This is useful when a workflow:

- crashes
- pauses for human input
- waits for a session message
- fails late in the pipeline
- needs to be resumed or inspected later

---

## Why checkpoints matter

Without checkpoints, a workflow that stops near the end usually has to restart from the beginning.

With checkpoints, Spectra can resume from the latest saved point.

That helps with:

- **crash recovery** — resume after process restarts
- **interrupts** — pause for approval or human input
- **sessions** — keep long-running conversations alive
- **debugging** — inspect saved execution state
- **time travel** — resume or fork from an earlier point

See [Time Travel](time-travel.md) for branching from old checkpoints.

---

## Enable checkpointing

```csharp
services.AddSpectra(builder =>
{
    // In-memory
    builder.AddInMemoryCheckpoints();

    // File-based
    builder.AddFileCheckpoints("./checkpoints");
});
```

Once a checkpoint store is registered, the runner saves checkpoints automatically based on the configured settings.

---

## When checkpoints are saved

By default, Spectra saves a checkpoint after every node.

You can change that behavior:

```csharp
builder.AddInMemoryCheckpoints(opts =>
{
    opts.Frequency = CheckpointFrequency.EveryNode;
    opts.CheckpointOnFailure = true;
    opts.CheckpointOnInterrupt = true;
    opts.CheckpointOnAwaitingInput = true;
});
```

| Frequency | Behavior |
| --- | --- |
| `EveryNode` | Save after every step. Safest and most complete |
| `StatusChangeOnly` | Save only when run status changes |
| `Disabled` | Do not save automatically |

For most workflows, `EveryNode` is the safest default.

---

## Checkpoint status

Each checkpoint records the current execution status.

| Status | Meaning | How to continue |
| --- | --- | --- |
| `InProgress` | Workflow was running or paused mid-run | `ResumeAsync(...)` |
| `Completed` | Workflow finished successfully | Cannot resume; fork instead |
| `Failed` | A step failed | Inspect, fix, then fork or rerun |
| `Interrupted` | Waiting for human input | `ResumeWithResponseAsync(...)` |
| `AwaitingInput` | Waiting for a session message | `SendMessageAsync(...)` |

This status is what tells the runner how a saved execution can continue.

---

## Resume a workflow

To continue from the latest checkpoint:

```csharp
var result = await runner.ResumeAsync(workflow, runId: "run-abc-123");
```

The runner loads the latest checkpoint for that run, restores workflow state, and continues from the saved next node.

If the run is already completed, resume does not continue it. In that case, use [forking](time-travel.md).

---

## What gets saved

A checkpoint stores everything needed to continue execution.

| Field | Purpose |
| --- | --- |
| `RunId` / `WorkflowId` | Identifies the run and workflow |
| `State` | Full `WorkflowState` snapshot |
| `LastCompletedNodeId` | The node that just finished |
| `NextNodeId` | The next node to execute |
| `StepsCompleted` | Number of completed steps |
| `Index` | Checkpoint number in the run |
| `Status` | Current lifecycle state |
| `PendingInterrupt` | Interrupt request waiting for a response |
| `ParentRunId` | Source run if this run was forked |
| `TenantId` / `UserId` | Identity context from the run |

At a practical level, this means Spectra saves both:

- **where the workflow was**
- **what the workflow knew at that moment**

---

## Retention and cleanup

You can control how many checkpoints are kept and how long they stay around.

```csharp
builder.AddFileCheckpoints("./checkpoints", opts =>
{
    opts.MaxCheckpointCount = 50;
    opts.RetentionPeriod = TimeSpan.FromDays(7);
});
```

You can also purge a run manually:

```csharp
await checkpointStore.PurgeAsync("run-abc-123");
```

Use retention settings to prevent old checkpoint history from growing without bound.

---

## Built-in stores

### In-memory

`InMemoryCheckpointStore` is fast and simple.

Use it for:

- development
- tests
- local experiments

Data is lost when the process stops.

### File-based

`FileCheckpointStore` writes checkpoints as JSON files on disk.

Use it for:

- local persistence
- development with restart safety
- simple self-hosted setups

### Production stores

For production, implement `ICheckpointStore` with your own backing store such as:

- Postgres
- Redis
- Cosmos DB
- DynamoDB

See [Build Your Own Checkpoint Store](../guides/build-your-own-checkpoint-store.md).

---

## A simple mental model

A checkpoint is just:

- saved workflow state
- saved position in the graph
- saved status for how execution should continue

That is why Spectra can resume instead of restart.

---

## What's next?

<div class="grid cards" markdown>

- **Time Travel**

  Resume or fork from earlier checkpoints.

  [:octicons-arrow-right-24: Time Travel](time-travel.md)

- **Interrupts**

  See how interrupted workflows pause and resume.

  [:octicons-arrow-right-24: Interrupts](interrupts.md)

- **Custom Checkpoint Store**

  Implement your own production-ready checkpoint backend.

  [:octicons-arrow-right-24: Custom Store Guide](../guides/build-your-own-checkpoint-store.md)

</div>