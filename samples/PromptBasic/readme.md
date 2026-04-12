# PromptBasic

The simplest possible LLM-powered workflow — a single `PromptStep` that summarizes text. One provider, one node, one call.

## What it demonstrates

- `PromptStep` (`stepType: "prompt"`) — single LLM completion, no tool loop
- `AddNode("summarize", "prompt", ...)` — registering a prompt node with parameters
- `AddAgent` + `agentId` parameter — binding a node to a provider/model/system prompt
- Reading the LLM response from `Context.summarize.response`
- Code-first workflow definition with `WorkflowBuilder`

## Prerequisites

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## Run it

```bash
cd samples/PromptBasic
dotnet run
```

## PromptStep vs AgentStep

This sample uses `PromptStep` (stepType `"prompt"`) — a single LLM call with no tool loop. Use it for summarization, translation, classification, or any task that doesn't need tools.

`AgentStep` (stepType `"agent"`) adds an autonomous tool-calling loop. Use it when the LLM needs to call functions iteratively. See the `SingleAgent` sample for that pattern.