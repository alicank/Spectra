---
description: "Resume from earlier checkpoints or fork new execution branches in Spectra."
---

# Time Travel & Forking

Spectra keeps the full checkpoint history of a run, not just the latest checkpoint.

That gives you two powerful options:

- **resume** from an earlier checkpoint in the same run
- **fork** a new run from an earlier checkpoint

This is useful when you want to retry, debug, compare outcomes, or safely experiment from a real workflow state.

---

## Resume vs fork

| Action | What it does | Best for |
| --- | --- | --- |
| **Resume** | Continues the same run from a chosen checkpoint | Retry and recovery |
| **Fork** | Creates a new run from a chosen checkpoint | Experiments, sandboxing, alternative paths |

A simple rule:

- use **resume** when you want to continue the same run
- use **fork** when you want to preserve the original and explore another path

---

## Checkpoint history

Each run stores a sequence of checkpoints with an `Index`.

```text
Run "abc-123"
  ├── 0  after classify
  ├── 1  after extract
  ├── 2  after summarize
  └── 3  after publish (failed)
```

You can inspect the history like this:

```csharp
var history = await checkpointStore.ListByRunAsync("abc-123");

var cp = await checkpointStore.LoadByIndexAsync("abc-123", index: 1);
```

That lets you inspect exactly what the workflow state looked like at a given point.

---

## Resume from a checkpoint

Use `ResumeFromCheckpointAsync(...)` to continue from a specific checkpoint in the same run.

```csharp
var result = await runner.ResumeFromCheckpointAsync(
    workflow,
    runId: "abc-123",
    checkpointIndex: 1);
```

In this example, the runner resumes from checkpoint `1` and continues from that checkpoint's `NextNodeId`.

The original run history stays intact, and new checkpoints are appended to the same run.

Use this when you want to:

- retry after a transient problem
- continue from a known-good point
- debug without restarting from the beginning

---

## Fork a new run

Use `ForkAndRunAsync(...)` to branch from a checkpoint into a new run.

```csharp
var result = await runner.ForkAndRunAsync(
    workflow,
    sourceRunId: "abc-123",
    checkpointIndex: 1,
    newRunId: "fork-xyz",
    stateOverrides: new WorkflowState
    {
        Inputs = new() { ["threshold"] = 0.9 }
    });
```

This creates a brand-new run that starts from the chosen checkpoint state.

The original run is unchanged.

Use this when you want to:

- test different inputs
- compare alternative outcomes
- sandbox a production run
- experiment safely without mutating history

---

## What forking does

When Spectra forks a run, it:

1. loads the source checkpoint
2. deep-clones its workflow state
3. applies any state overrides
4. assigns the new run ID
5. saves the new run's first checkpoint
6. records `ParentRunId` for lineage
7. continues execution from the saved `NextNodeId`

The important part is that the forked run is independent. It does not share mutable state with the source run.

---

## Trace lineage

Forked runs keep lineage through `ParentRunId`.

You can inspect that chain:

```csharp
var lineage = await checkpointStore.GetLineageAsync("fork-xyz");
```

This lets you trace how a run was derived from earlier runs.

That is useful for:

- audits
- debugging
- experimentation history
- production-to-sandbox traceability

---

## Practical examples

### Retry after a failure

Go back to a checkpoint before the failure and continue the same run:

```csharp
var result = await runner.ResumeFromCheckpointAsync(
    workflow,
    runId,
    checkpointIndex: 2);
```

### Correct bad input and branch

Fork from a checkpoint and override the bad input:

```csharp
var result = await runner.ForkAndRunAsync(
    workflow,
    runId,
    checkpointIndex: 1,
    newRunId: "corrected-run",
    stateOverrides: new WorkflowState
    {
        Inputs = new() { ["data"] = correctedData }
    });
```

### Sandbox from production

Create a safe experimental branch from a production run:

```csharp
var sandbox = await runner.ForkAndRunAsync(
    workflow,
    prodRunId,
    checkpointIndex: 5,
    newRunId: "sandbox-run",
    stateOverrides: new WorkflowState
    {
        Inputs = new() { ["budget"] = 50_000 }
    });
```

---

## A simple mental model

A checkpoint is a saved execution point.

From that point, Spectra can either:

- **move forward in the same timeline** with resume
- **create a new timeline** with fork

That is the core idea behind time travel.

---

## What's next?

<div class="grid cards" markdown>

- **Checkpointing**

  Learn how checkpoints are stored and configured.

  [:octicons-arrow-right-24: Checkpointing](checkpointing.md)

- **Interrupts**

  Pause and resume workflows around human input.

  [:octicons-arrow-right-24: Interrupts](interrupts.md)

- **Experimentation**

  Use forking for safe branching and A/B-style testing.

  [:octicons-arrow-right-24: Experimentation](../advanced/experimentation.md)

</div>