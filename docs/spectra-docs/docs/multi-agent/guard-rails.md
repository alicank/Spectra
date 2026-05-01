---
description: "Configure guard rails for multi-agent workflows in Spectra, including depth limits, cycle control, token budgets, timeouts, and escalation."
---

# Guard Rails

Multi-agent workflows need limits.

Without guard rails, agents can:

- hand off in circles
- delegate too deeply
- consume too many tokens
- run too long
- access more state than they should

Spectra provides guard rails at both the workflow and agent level to keep multi-agent execution bounded and predictable.

---

## What guard rails control

| Risk | Guard rail |
| --- | --- |
| Too many handoffs | Handoff chain depth |
| Too much nested delegation | Delegation depth |
| Revisiting the same agents in loops | Cycle detection |
| Excessive token usage | Global token budget |
| Too many LLM calls overall | Max total agent iterations |
| Agents running too long | Per-agent timeout |
| Agents seeing or changing too much state | State access control |
| Unresolved tasks | Escalation |

---

## Routing limits

### Handoff chain depth

Limits how many times control can transfer between agents in one handoff chain.

```csharp
WorkflowBuilder.Create("pipeline")
    .WithMaxHandoffChainDepth(5)
```

Default: `10`

You can also override this per agent:

```csharp
agent.WithMaxHandoffChainDepth(3)
```

If the limit is reached, the handoff is blocked and the agent must continue without transferring.

### Delegation depth

Limits how deeply supervisors can nest delegations.

```csharp
agent.WithMaxDelegationDepth(2)
```

Default: `3`

If the limit is reached, further delegation is rejected and the supervisor must continue with the available information.

### Cycle detection

Controls whether an agent can be revisited in a handoff chain.

```csharp
agent.WithCyclePolicy(CyclePolicy.Deny)
agent.WithCyclePolicy(CyclePolicy.AllowWithLimit(2))
agent.WithCyclePolicy(CyclePolicy.Allow)
```

| Policy | Behavior |
| --- | --- |
| `Deny` | No agent can appear twice in the chain |
| `AllowWithLimit(n)` | An agent can be revisited up to `n` times |
| `Allow` | Revisits are unrestricted |

Default is strict cycle denial.

When a cycle is blocked, the handoff is rejected and the agent must find another path.

---

## Cost and execution limits

### Global token budget

Caps total token usage across the workflow run.

```csharp
WorkflowBuilder.Create("expensive-pipeline")
    .WithGlobalTokenBudget(200_000)
```

This budget is shared across handoffs and delegations.

If the budget is exhausted, further delegated work is blocked.

You can also set node-level budgets:

```csharp
.AddAgentNode("research", "researcher", node => node
    .WithTokenBudget(50_000))
```

### Max total agent iterations

Limits total LLM calls across all agent activity in the workflow.

```csharp
WorkflowBuilder.Create("team-project")
    .WithMaxTotalAgentIterations(200)
```

Default: `500`

This is useful when many agents each have generous per-node limits but you still want a hard cap on overall activity.

---

## Runtime limits

### Per-agent timeout

Limits the total wall-clock time for a single agent execution.

```csharp
agent.WithTimeout(TimeSpan.FromMinutes(5))
```

This is separate from single-call retry or timeout settings.

Use it when you want to cap the full agent run, including:

- multiple LLM iterations
- tool calls
- internal waiting within the step

---

## Data boundaries

### State access control

Restrict which parts of workflow state an agent can read or write.

```csharp
agent
    .WithStateReadPaths("Context.*", "Inputs.task")
    .WithStateWritePaths("Context.results")
```

Supports wildcards.

By default, access is unrestricted.

Use this when:

- agents should not see each other's data
- only one agent should write to a specific state area
- you want least-privilege boundaries in multi-agent workflows

---

## Escalation

When an agent cannot finish normally, it can escalate.

```csharp
agent.WithEscalationTarget("senior-analyst")
agent.WithEscalationTarget("human")
```

### Escalate to another agent

Transfers the task to a more capable or more appropriate agent.

### Escalate to a human

Using `"human"` triggers an interrupt so a person can review and resume the workflow.

Both escalation paths emit `AgentEscalationEvent`.

---

## Under the hood: `AgentExecutionContext`

Spectra tracks guard-rail state in an `AgentExecutionContext` that flows through handoff and delegation chains.

| Field | Purpose |
| --- | --- |
| `ChainDepth` | Current handoff depth |
| `DelegationDepth` | Current delegation depth |
| `TotalTokensConsumed` | Cumulative token usage |
| `GlobalBudgetRemaining` | Remaining token budget |
| `VisitedAgents` | Used for cycle detection |
| `HandoffHistory` | Audit trail of handoffs |
| `WallClockDeadline` | Deadline for the chain or session |
| `CyclePolicy` | Active cycle detection policy |
| `OriginatorRunId` | Run ID of the originating workflow execution |
| `ParentAgentId` | Parent agent in the current chain |

This context is copied when passed into child agent execution so each delegation branch can be tracked safely.

---

## Putting it together

```csharp
var workflow = WorkflowBuilder.Create("guarded-pipeline")
    .WithMaxHandoffChainDepth(5)
    .WithGlobalTokenBudget(200_000)
    .WithMaxTotalAgentIterations(100)

    .AddAgent("researcher", "openai", "gpt-4o", agent => agent
        .WithHandoffTargets("coder")
        .WithCyclePolicy(CyclePolicy.Deny)
        .WithConversationScope(ConversationScope.LastN, maxMessages: 5)
        .WithTimeout(TimeSpan.FromMinutes(3)))

    .AddAgent("coder", "anthropic", "claude-sonnet-4-20250514", agent => agent
        .WithHandoffTargets("reviewer")
        .WithEscalationTarget("human")
        .WithMaxDelegationDepth(1))

    .AddAgent("reviewer", "openai", "gpt-4o", agent => agent
        .WithCyclePolicy(CyclePolicy.AllowWithLimit(1))
        .WithHandoffTargets("coder"))

    .Build();
```

This example combines:

- a handoff depth limit
- a global budget
- a total iteration cap
- strict cycle handling
- per-agent timeout
- human escalation

That is a good default style for production-oriented multi-agent workflows.

---

## A practical mental model

Use guard rails in layers:

- **routing limits** keep coordination bounded
- **budget limits** keep cost bounded
- **timeouts** keep runtime bounded
- **state controls** keep access bounded
- **escalation** gives the workflow a safe fallback

That combination is what makes autonomous behavior usable in real systems.

---

## What's next?

<div class="grid cards" markdown>

- **Multi-Agent Overview**

  Learn how handoff and delegation work together.

  [:octicons-arrow-right-24: Overview](overview.md)

- **Interrupts**

  Pause workflows for human approval or intervention.

  [:octicons-arrow-right-24: Interrupts](../execution/interrupts.md)

- **Events**

  Observe agent behavior, handoffs, and escalation.

  [:octicons-arrow-right-24: Events](../observability/events.md)

</div>