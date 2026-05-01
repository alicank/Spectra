# Multi-Agent

Spectra supports multi-agent workflows where multiple LLM agents collaborate, delegate, and hand off work to each other.

## Agent Definitions

An agent is configured with a provider, model, and system prompt. The provider and model are positional arguments to `AddAgent`; tools are configured on the agent node (not the agent definition):

```csharp
services.AddSpectra(builder =>
{
    builder.AddAgent("planner", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("You are a project planner. Break tasks into subtasks.")
        .AsSupervisor("coder"));   // auto-injects delegate_to_agent tool

    builder.AddAgent("coder", "anthropic", "claude-sonnet-4-20250514", agent => agent
        .WithSystemPrompt("You are a senior developer. Write clean, tested code."));
});
```

## Handoff Patterns

### Transfer (one-way)

The current agent transfers control to another agent. The original agent does not get control back. The `transfer_to_agent` tool is **automatically injected** when `WithHandoffTargets` is set — you do not add it manually:

```csharp
builder.AddAgent("router", "openai", "gpt-4o", agent => agent
    .WithSystemPrompt("Route the user's request to the right specialist.")
    .WithHandoffTargets("coder", "analyst")
    .WithHandoffPolicy(HandoffPolicy.Allowed));
```

When the router calls `transfer_to_agent`, the workflow switches to the target agent for the remainder of the step.

### Delegation (round-trip)

The current agent delegates a subtask to another agent and gets the result back. The `delegate_to_agent` tool is **automatically injected** when `AsSupervisor` is called — you do not add it manually:

```csharp
builder.AddAgent("lead", "openai", "gpt-4o", agent => agent
    .WithSystemPrompt("You are a team lead.")
    .AsSupervisor("coder", "analyst")
    .WithDelegationPolicy(DelegationPolicy.Allowed));
```

When the lead calls `delegate_to_agent`, the target worker executes and returns its result to the lead, who continues its own reasoning.

## Supervisor Architecture

Build a supervisor that coordinates multiple workers:

```csharp
var workflow = WorkflowBuilder.Create("supervised")
    .AddAgent("planner", "openai", "gpt-4o", agent => agent
        .AsSupervisor("frontend-dev", "backend-dev", "code-reviewer"))
    .AddAgent("frontend-dev", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("You are a frontend developer."))
    .AddAgent("backend-dev", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("You are a backend developer."))
    .AddAgent("code-reviewer", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("You are a code reviewer."))
    .AddAgentNode("supervisor", "planner", node => node
        .WithUserPrompt("{{inputs.task}}")
        .WithMaxIterations(20))
    .SetEntryNode("supervisor")
    .Build();
```

## Agent Builder (Code-First)

The `AgentNodeBuilder` provides a fluent API for defining agent nodes. The agent identity (`provider`, `model`, `systemPrompt`) is set on the agent definition via `AddAgent`; the node builder controls runtime behaviour like tools and iteration limits:

```csharp
var workflow = WorkflowBuilder.Create("multi-agent")
    .AddAgent("analyst", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("Analyze the data and produce insights.")
        .AsSupervisor("worker"))   // auto-injects delegate_to_agent
    .AddAgentNode("analyst-node", "analyst", node => node
        .WithTools("query_database")
        .WithMaxIterations(10))
    .Build();
```

## Handoff Events

Every handoff emits events for observability:

- `AgentHandoffEvent` — Agent A initiates a transfer to Agent B
- `AgentDelegationStartedEvent` — A supervisor starts delegating a task to a worker
- `AgentDelegationCompletedEvent` — A delegated worker finishes and returns its result
- `AgentEscalationEvent` — An agent escalates due to failure, budget exhaustion, or hitting max iterations
- `AgentHandoffBlockedEvent` — A handoff or delegation was blocked (e.g. cycle denied, unknown target, policy)

## Conversation Scope

Control how much conversation history flows to the target agent on handoff:

```csharp
builder.AddAgent("coder", "openai", "gpt-4o", agent => agent
    .WithConversationScope(ConversationScope.Handoff)   // only the handoff payload (default)
    // or
    .WithConversationScope(ConversationScope.Full)      // full conversation history
    // or
    .WithConversationScope(ConversationScope.Summary)   // a generated summary
    // or
    .WithConversationScope(ConversationScope.LastN, maxMessages: 5) // last N messages
);
```
