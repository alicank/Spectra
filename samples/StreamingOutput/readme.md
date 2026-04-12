# StreamingOutput

Streams LLM tokens to the console in real-time — no waiting for the full response. One provider, one node, live output.

## What it demonstrates

- `runner.StreamAsync()` instead of `runner.RunAsync()` — returns `IAsyncEnumerable<WorkflowEvent>`
- `StreamMode.Tokens` — yields every event type including per-token deltas
- `TokenStreamEvent` — carries `Token` (text chunk) and `TokenIndex` (sequential position)
- `StepCompletedEvent` — fires when a node finishes, includes `Duration`
- `WorkflowCompletedEvent` — fires when the entire workflow completes
- Pattern matching on `WorkflowEvent` subtypes for consumer-side filtering

## Stream Modes

| Mode | What's Included | Use Case |
|------|----------------|----------|
| `Tokens` | Everything — including individual token deltas | Live LLM output in chat UIs |
| `Messages` | Everything except `TokenStreamEvent` | Step-level progress without per-token noise |
| `Updates` | `StepCompleted`, `StateChanged`, `WorkflowCompleted` only | Progress bars, dashboards |
| `Values` | `StateChanged` and `WorkflowCompleted` only | Reactive state consumers |
| `Custom` | Everything (same as Tokens) | Consumer-side filtering |

## Prerequisites

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## Run it

```bash
cd samples/StreamingOutput
dotnet run
```

## StreamAsync vs RunAsync

`RunAsync` blocks until the entire workflow completes, then returns a `WorkflowResult` with the final state. Use it for batch processing or when you don't need intermediate visibility.

`StreamAsync` yields events as they happen — including individual LLM tokens. Use it for real-time UIs, SSE endpoints, progress indicators, or any scenario where latency-to-first-token matters.

Both methods produce identical final state. Streaming doesn't change what the workflow does — only how you observe it.