# JsonVsCode

The same "greet and translate" workflow defined twice — once with the fluent `WorkflowBuilder` API, once in JSON — both producing the same result. Demonstrates that C# and JSON are interchangeable representations of the same `WorkflowDefinition`.

## What it demonstrates

- `WorkflowBuilder.Create()` — code-first workflow definition with `AddAgent`, `AddAgentNode`, `AddEdge`
- `JsonFileWorkflowStore` — loading the same workflow from a `.workflow.json` file
- Both definitions produce a `WorkflowDefinition` with identical structure
- Agent registration via `AddOpenRouter` — the provider is shared by both paths
- Cross-node state references using `{{Context.greet.response}}`

## Prerequisites

Set your OpenRouter API key:

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## The graph (same for both)

```
┌──────────┐     ┌──────────────┐
│  greet   │────▶│  translate   │
└──────────┘     └──────────────┘
  agent:assistant   agent:assistant
  "Say hello..."    "Translate to French..."
```

## Run it

```bash
cd samples/JsonVsCode
dotnet run
```

## What to look for

- Path A (code) and Path B (JSON) both run the same two-step workflow
- The greeting text will differ (LLM is non-deterministic) but the structure is identical
- The comparison section confirms both definitions have the same node count, edge count, and agent count
- This is Spectra's core identity: one orchestration model, two entry points