# CyclicLoop

Retries an operation until the quality score meets a threshold. Demonstrates cyclic graphs with loopback edges and `MaxNodeIterations` as a safety guard.

## What it demonstrates

- Loopback edges with `isLoopback: true` — enables cycles without breaking topological sort
- Conditional loopback: loops back when `Context.check.needsRetry == true`
- Exit condition: continues to `done` when `Context.check.needsRetry == false`
- `MaxNodeIterations` prevents runaway loops (set to 5 in this sample)
- State accumulation across loop iterations (attempt count, improving scores)

## The graph

```
┌──────────┐      ┌──────────┐
│ attempt  │─────▶│  check   │
└──────────┘      └──────────┘
     ▲                 │
     │  needsRetry     │  !needsRetry
     └─────────────────┘       │
                               ▼
                          ┌──────────┐
                          │   done   │
                          └──────────┘
```

## Run it

```bash
cd samples/CyclicLoop
dotnet run
```

Expected output — the score improves each attempt until it passes the 0.8 threshold:

```
  [attempt] Try #1 → score: 0.5
  [check] Score 0.5 vs threshold 0.8 → RETRY
  [attempt] Try #2 → score: 0.7
  [check] Score 0.7 vs threshold 0.8 → RETRY
  [attempt] Try #3 → score: 0.9
  [check] Score 0.9 vs threshold 0.8 → PASS
  [done] Accepted after 3 attempt(s) with score 0.9
```

## What to look for

- The `BranchEvaluatedEvent` lines show the loopback condition being evaluated each iteration
- The loop exits naturally when the score exceeds the threshold
- If you lower the score improvement rate (in `AttemptStep`) so it never passes, the workflow stops after 5 iterations with a `MaxNodeIterations` error — that's the safety guard