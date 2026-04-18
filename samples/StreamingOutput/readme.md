````markdown
# StreamingOutput

Streams LLM tokens to the console in real time.

This sample uses `runner.StreamAsync(...)` instead of `RunAsync(...)`, so output appears as it is generated.

## What it demonstrates

- streaming workflow events with `runner.StreamAsync(...)`
- using `StreamMode.Tokens` for live token output
- handling `TokenStreamEvent` as tokens arrive
- handling `StepCompletedEvent` and `WorkflowCompletedEvent`
- counting streamed tokens and completed steps

## Flow

```mermaid
flowchart LR
    A[workflow input] --> B[prompt node]
    B --> C[token stream]
    C --> D[console output]
    B --> E[step completed]
    E --> F[workflow completed]
````

## Prerequisites

Set `OPENROUTER_API_KEY` before running the sample.

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

## Example output

```text
--- Streaming tokens ---

Graph-based AI orchestration is a method for designing and managing complex AI workflows by modeling them as directed graphs...

[explain] completed in 00:00:11.4025694

Workflow finished — success: True

Tokens streamed: 129 | Steps completed: 1
```

```
```
