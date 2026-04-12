# InterruptApproval

A content publishing pipeline that pauses for human review before publishing. Demonstrates declarative interrupts with `InterruptBefore`, checkpoint-on-interrupt, and `ResumeWithResponseAsync`.

## What it demonstrates

- Declarative `interruptBefore` on a node — pauses execution without any step code
- `CheckpointStatus.Interrupted` — the checkpoint stores the pending `InterruptRequest`
- `ResumeWithResponseAsync` — resumes with an `InterruptResponse` (approved/rejected)
- `InterruptResponse.ApprovedResponse()` — factory method with respondedBy and comment
- `StepInterruptedEvent` in the console showing the pause reason

## The graph

```
┌──────────┐     ┌──────────────────────────┐
│  draft   │────▶│  publish                 │
└──────────┘     │  (interruptBefore: ...)   │
                 │                          │
                 │  ⏸ PAUSES HERE           │
                 │  waits for approval       │
                 │  then executes            │
                 └──────────────────────────┘
```

## What happens

1. **Run 1** — `draft` generates content. The runner reaches `publish` but sees `interruptBefore`. It checkpoints with status `Interrupted` and stops. The `publish` step never executes.

2. **Checkpoint inspection** — the program reads the checkpoint and prints the `PendingInterrupt` with its reason and title.

3. **Run 2** — calls `ResumeWithResponseAsync` with an approved response. The runner loads the interrupted checkpoint, sees the approval, skips the interrupt, and executes `publish`.

## Run it

```bash
cd samples/InterruptApproval
dotnet run
```

## What to look for

- Run 1: `draft` runs but `publish` does NOT — the `StepInterruptedEvent` shows the pause
- Checkpoint status is `Interrupted` with a `PendingInterrupt` containing the reason
- Run 2: `publish` runs immediately after the interrupt is cleared by the approval
- The approval's `respondedBy` and `comment` are passed through the `InterruptResponse`