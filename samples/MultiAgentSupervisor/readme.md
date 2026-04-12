# MultiAgentSupervisor

A supervisor agent coordinates two specialist workers — a researcher and a writer — to produce a complete RFP (Request for Proposal) response. The supervisor delegates tasks via the built-in `delegate_to_agent` tool and synthesizes the results.

## What it demonstrates

- **Supervisor pattern** — `AsSupervisor("researcher", "writer")` configures the supervisor with named workers
- **`DelegationPolicy.Allowed`** — the supervisor can delegate freely without approval gates
- **`delegate_to_agent` tool** — auto-injected when `SupervisorWorkers` is non-empty; the LLM calls it to dispatch subtasks
- **Inline agent execution** — workers run inside the supervisor's tool loop (recursive `AgentStep`), not as separate workflow nodes
- **`AgentDelegationStartedEvent` / `AgentDelegationCompletedEvent`** — emitted for each delegation, showing worker agent, task, tokens consumed, and duration
- **Token tracking** — the supervisor's final output includes cumulative tokens across all agents

## Prerequisites

Set your OpenRouter API key:

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## The flow

```
┌──────────────────┐
│   supervisor     │
│  (coordinator)   │
│                  │
│  1. delegate     │──── delegate_to_agent("researcher", task) ────┐
│     research     │                                                │
│                  │◄── researcher returns findings ───────────────┘
│                  │
│  2. delegate     │──── delegate_to_agent("writer", task) ───────┐
│     writing      │                                                │
│                  │◄── writer returns draft ──────────────────────┘
│                  │
│  3. synthesize   │
│     final answer │
└──────────────────┘
```

All three agents share the same provider (OpenRouter → gpt-4o-mini) but each has its own system prompt and role.

## Run it

```bash
cd samples/MultiAgentSupervisor
dotnet run
```

## What to look for

- **`AgentDelegationStartedEvent`** — shows the supervisor dispatching to `researcher` with a task description
- **`AgentDelegationCompletedEvent`** — shows the researcher's result summary and tokens consumed
- **Second delegation** — the supervisor then dispatches to `writer` with the research findings
- **Final response** — the supervisor synthesizes both outputs into an executive summary
- **Iteration count** — typically 5 iterations: call LLM → delegate researcher → call LLM → delegate writer → call LLM (final)
- **Token totals** — cumulative across supervisor + researcher + writer

## Enterprise use case

This pattern maps directly to real workflows: sales teams responding to RFPs, consulting firms producing deliverables, or any scenario where a coordinator breaks work into specialist subtasks. Replace the agents with domain-specific experts (legal reviewer, pricing analyst, technical architect) and the pattern scales.

## Next steps

- [Agent Step](../../docs/spectra-docs/docs/llm/agent-step.md) — how the autonomous agent loop works
- [Multi-Agent](../../docs/spectra-docs/docs/concepts/multi-agent.md) — delegation, handoff, and conversation scope