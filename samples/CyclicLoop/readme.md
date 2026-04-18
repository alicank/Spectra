````markdown id="ol0q3n"
# CyclicLoop

Retries an operation until it passes a quality check.

This sample loads a cyclic workflow from JSON, runs custom steps, and loops until the score meets the configured threshold.

## What it demonstrates

- loading a cyclic workflow from JSON
- registering custom step types with `AddStep(...)`
- using conditional edges to loop back
- using `isLoopback: true` for cycles
- stopping when a condition passes
- using `maxNodeIterations` as a loop safety limit

## Flow

```mermaid
flowchart LR
    A[attempt] --> B[check]
    B -->|needsRetry == true| A
    B -->|needsRetry == false| C[done]
````

## Run it

```bash
cd samples/CyclicLoop
dotnet run
```

## Example output

```text id="4zkqg2"
  [attempt] Try #1 → score: 0.5
  [check] Score 0.5 vs threshold 0.8 → RETRY
  [attempt] Try #2 → score: 0.7
  [check] Score 0.7 vs threshold 0.8 → RETRY
  [attempt] Try #3 → score: 0.9
  [check] Score 0.9 vs threshold 0.8 → PASS
  [done] Accepted after 3 attempt(s) with score 0.9

Errors: 0
```

```
```
