# AgentWithFallback

An incident response pipeline that classifies and summarizes infrastructure incidents using two resilient LLM routing strategies — RoundRobin load-balancing across models and Failover across providers with a quality gate.

## What it demonstrates

- **RoundRobin fallback policy** — the `classify` node rotates between `claude-sonnet-4-20250514` and `claude-haiku-3-5-20241022` on each request; if the selected model fails, the other is tried automatically
- **Failover fallback policy** — the `summarize` node tries Anthropic first; on failure (or if the quality gate rejects the response), it falls back to OpenRouter's `gpt-4o-mini`
- **MinLengthQualityGate** — rejects summaries shorter than 50 characters, triggering a transparent fallback to the next provider
- **FallbackTriggeredEvent / QualityGateRejectedEvent** — visible in the console event stream when a fallback activates
- **Multi-provider registration** — `AddAnthropic` + `AddOpenRouter` in the same `AddSpectra` block
- **JSON workflow with `fallbackPolicy` parameter** — the policy name is set per-node in the workflow JSON, not in code

## Prerequisites

Set both API keys:

```bash
# bash
export ANTHROPIC_API_KEY="your-anthropic-key"
export OPENROUTER_API_KEY="your-openrouter-key"

# PowerShell
$env:ANTHROPIC_API_KEY="your-anthropic-key"
$env:OPENROUTER_API_KEY="your-openrouter-key"
```

## The graph

```
┌────────────┐     ┌──────────────┐
│  classify   │────▶│  summarize   │
└────────────┘     └──────────────┘
  RoundRobin         Failover
  sonnet ↔ haiku     anthropic → openrouter
                     + MinLengthQualityGate(50)
```

## Run it

```bash
cd samples/AgentWithFallback
dotnet run
```

Or pass a custom incident report:

```bash
dotnet run -- "Redis cluster in eu-west-1 lost quorum at 14:30 UTC. 3 of 5 nodes unreachable. Cache miss rate spiked to 87%. Application latency increased 4x."
```

## What to look for

- **RoundRobin rotation** — run the sample multiple times; the `classify` node alternates which model it starts with (check the `model` field in the output and the `FallbackTriggeredEvent` if the first model fails)
- **Quality gate** — if the summarize response is shorter than 50 characters, you'll see a `QualityGateRejectedEvent` followed by a `FallbackTriggeredEvent` as it switches from Anthropic to OpenRouter
- **Transparent failover** — temporarily set an invalid `ANTHROPIC_API_KEY` to force all requests through the OpenRouter fallback path; the workflow completes normally with a different model

## Fallback policies explained

| Policy | Strategy | Behaviour |
|--------|----------|-----------|
| `load-balanced` | RoundRobin | Rotates the starting model on each request. If selected model fails, cascades to the next. |
| `failover-chain` | Failover | Always tries providers in order. Moves to next only on failure or quality gate rejection. |

## Next steps

- [LLM Resilience](../../docs/spectra-docs/docs/llm/resilience.md) — retry, caching, and fallback deep-dive
- [Provider Fallback](../../docs/spectra-docs/docs/resilience/fallback.md) — weighted strategies, split routing, custom quality gates