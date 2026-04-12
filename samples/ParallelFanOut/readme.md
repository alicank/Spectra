# ParallelFanOut

Analyzes a document through two parallel branches (sentiment + entity extraction), then merges the results. Demonstrates fan-out/fan-in with the `ParallelScheduler`.

## What it demonstrates

- Fan-out: one node feeds two independent branches
- Fan-in: a merge node with `WaitForAll: true` waits for both branches
- `ParallelScheduler` runs independent nodes concurrently via `Task.WhenAll`
- `ParallelBatchStartedEvent` / `ParallelBatchCompletedEvent` show batch-level execution
- Thread-safe state access (parallel branches write to separate `Context` keys)

## The graph

```
                    ┌─────────────┐
               ┌───▶│  sentiment  │───┐
┌──────────┐   │    └─────────────┘   │  ┌──────────┐
│  fetch   │───┤                      ├─▶│  merge   │ (WaitForAll)
└──────────┘   │    ┌─────────────┐   │  └──────────┘
               └───▶│  entities   │───┘
                    └─────────────┘
```

## Run it

```bash
cd samples/ParallelFanOut
dotnet run
```

## What to look for

Both `sentiment` and `entities` have a 500ms simulated delay. If they ran sequentially, total time would be ~1000ms. With parallel execution, the batch completes in ~500ms — check the `ParallelBatchCompletedEvent` duration to verify.

The `merge` node only appears in the next batch after both branches complete, because `waitForAll` is `true`.