````markdown
# PromptBasic

The smallest LLM-powered Spectra sample.

This project defines a workflow in code, runs a single `prompt` node, and prints the model response.

## What it demonstrates

- registering OpenRouter with `AddOpenRouter(...)`
- defining a workflow with `WorkflowBuilder`
- configuring an agent with `AddAgent(...)`
- running a single `prompt` node
- passing input with `WorkflowState`
- reading the result from `result.Context["summarize"]["response"]`

## Prerequisites

Set `OPENROUTER_API_KEY` before running the sample.

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
````

## Run it

```bash
cd samples/PromptBasic
dotnet run
```

## Example output

```text
[WorkflowStartedEvent] ...
[StepStartedEvent] ...
[StepCompletedEvent] ...
[StateChangedEvent] ...
[WorkflowCompletedEvent] ...

Summary: Spectra is an open-source .NET framework for defining and orchestrating AI workflows as explicit graphs using C# or JSON. It allows users to combine code and AI steps, select providers per step, and supports advanced features like sequential/parallel/cyclic execution, checkpointing, human-in-the-loop interrupts, and multi-agent handoffs.
Errors: 0
```

```
```
