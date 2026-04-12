# SingleAgent

An autonomous agent with two custom tools — a **calculator** and a **clock**. The agent receives a multi-part question, reasons about it, calls tools in a loop, and synthesizes a final answer.

## What it demonstrates

- **AgentStep** (`stepType: "agent"`) — the autonomous tool-calling loop
- **Custom tools** — implementing `ITool` with `ToolDefinition` and `ToolResult`
- **Tool registration** — `spectra.AddTool(...)` at startup
- **Tool whitelist** — `WithParameter("tools", ...)` restricts which tools the agent sees
- **maxIterations** — safety guard that caps the number of LLM↔tool round trips
- **Parallel tool execution** — when the LLM returns multiple tool calls in one turn, they run concurrently
- **Multi-step reasoning** — the agent breaks a complex question into tool calls, observes results, then answers

## The scenario

A dinner party planning question that requires:

1. **Math** — calculate total food needed (7 people × 350g × 1.5x safety margin → kilograms)
2. **Time check** — get the current time to advise on shopping feasibility

The agent must call the calculator multiple times for the arithmetic chain, call the clock once, then synthesize everything into a coherent answer.

## AgentStep vs PromptStep

| | PromptStep | AgentStep |
|---|---|---|
| LLM calls | Exactly 1 | 1 to `maxIterations` |
| Tools | None | Any registered tools |
| Use case | Summarization, classification, translation | Research, planning, multi-step tasks |

## Prerequisites

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## Run it

```bash
cd samples/SingleAgent
dotnet run
```

## Expected output

The agent will make several iterations:

1. **Iteration 1** — calls `calculator(7, 350, multiply)` and `clock()` (in parallel)
2. **Iteration 2** — calls `calculator(2450, 1.5, multiply)` with the intermediate result
3. **Iteration 3** — calls `calculator(3675, 1000, divide)` to convert grams → kilograms
4. **Final** — synthesizes: "You need 3.675 kg of food. It's currently [time] — [shopping advice]."

The exact iteration count depends on the model's reasoning — it may combine steps.

## Key outputs

| Output | Description |
|--------|-------------|
| `response` | The agent's final synthesized answer |
| `iterations` | Number of LLM↔tool round trips taken |
| `stopReason` | `"end_turn"` (finished naturally) or `"max_iterations"` (hit the cap) |
| `messages` | Full conversation history including all tool calls and results |