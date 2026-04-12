# CheckpointResume

An order processing pipeline that pauses mid-execution and resumes from a file-based checkpoint. Demonstrates `StepStatus.NeedsContinuation`, `FileCheckpointStore`, and `ResumeAsync`.

## What it demonstrates

- `FileCheckpointStore` — checkpoints persisted to disk as JSON files
- `StepStatus.NeedsContinuation` — a step signals the runner to pause and checkpoint
- `ResumeAsync(workflow, runId)` — resumes execution from the last saved checkpoint
- Checkpoint inspection — loading and reading checkpoint metadata (status, next node, steps completed)
- State survives across runs — the resumed execution picks up exactly where it left off

## The graph

```
┌──────────┐     ┌──────────┐     ┌──────────┐
│ validate │────▶│ process  │────▶│ confirm  │
└──────────┘     └──────────┘     └──────────┘
                      │
                 (pauses here)
```

## What happens

The program runs the workflow **twice** in a single execution:

1. **Run 1** — starts fresh. `validate` succeeds. `process` returns `NeedsContinuation` (simulating a pending payment). The runner checkpoints and stops.

2. **Checkpoint inspection** — the program reads the saved checkpoint from disk and prints its status, next node, and steps completed.

3. **Run 2** — calls `ResumeAsync` with the same `runId`. The runner loads the checkpoint, restores state, and re-executes `process`. This time `process` succeeds (payment confirmed). `confirm` runs and the workflow completes.

## Run it

```bash
cd samples/CheckpointResume
dotnet run
```

## What to look for

- Run 1 executes `validate` and `process`, then stops — `confirm` never runs
- The checkpoint shows `Status: InProgress` and `NextNode: process`
- Run 2 skips `validate` entirely and picks up at `process`
- The `__processAttempted` marker in state is how the step knows it's the second call