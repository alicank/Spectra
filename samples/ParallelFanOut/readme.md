````markdown
# ParallelFanOut

Analyzes a document through two parallel branches, then merges the results.

This sample uses `ParallelScheduler` to fan out into `sentiment` and `entities`, then fan in at `merge`.

## What it demonstrates

- running a workflow with `ParallelScheduler`
- fanning out into independent branches
- merging branch results with `waitForAll: true`
- registering custom parallel-safe step types with `AddStep(...)`
- writing branch outputs into separate `Context` keys

## Flow

```mermaid
flowchart LR
    A[fetch] --> B[sentiment]
    A --> C[entities]

    B --> D[merge]
    C --> D

    B -.->|waitForAll| D
    C -.->|waitForAll| D
````

## Run it

```bash
cd samples/ParallelFanOut
dotnet run
```

## Example output

```text
  [analyze:fetch] Starting 'fetch' ...
  [analyze:fetch] Completed 'fetch' → done: fetch
  [analyze:sentiment] Starting 'sentiment' ...
  [analyze:entities] Starting 'entities' ...
  [analyze:entities] Completed 'entities' → [Spectra, .NET, AI]
  [analyze:sentiment] Completed 'sentiment' → positive (confidence: 0.87)
  [merge] Combining: sentiment=positive (confidence: 0.87), entities=[Spectra, .NET, AI]

── Results ─────────────────────────────────────────────
  Sentiment : positive (confidence: 0.87)
  Entities  : [Spectra, .NET, AI]
  Merged at : 2026-04-17T06:46:27.1565463+00:00
  Errors    : 0
```

```
```
