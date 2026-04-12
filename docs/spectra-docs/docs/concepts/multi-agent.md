# Multi-Agent

Spectra supports multi-agent workflows where multiple LLM agents collaborate, delegate, and hand off work to each other.

## Agent Definitions

An agent is configured with a provider, model, system prompt, and available tools:

```csharp
services.AddSpectra(builder =>
{
    builder.AddAgent("planner", agent => agent
        .WithProvider("openai")
        .WithModel("gpt-4o")
        .WithSystemPrompt("You are a project planner. Break tasks into subtasks.")
        .WithTools("delegate_to_agent"));

    builder.AddAgent("coder", agent => agent
        .WithProvider("anthropic")
        .WithModel("claude-sonnet-4-20250514")
        .WithSystemPrompt("You are a senior developer. Write clean, tested code.")
        .WithTools("read_file", "write_file", "run_tests"));
});
```

## Handoff Patterns

### Transfer (one-way)

The current agent transfers control to another agent. The original agent does not get control back:

```csharp
// The "transfer_to_agent" tool is built-in
builder.AddAgent("router", agent => agent
    .WithTools("transfer_to_agent")
    .WithSystemPrompt("Route the user's request to the right specialist."));
```

When the router calls `transfer_to_agent(target: "coder")`, the workflow switches to the coder agent for the remainder of the step.

### Delegation (round-trip)

The current agent delegates a subtask to another agent and gets the result back:

```csharp
builder.AddAgent("lead", agent => agent
    .WithTools("delegate_to_agent")
    .WithDelegationPolicy(DelegationPolicy.AllowAll));
```

When the lead calls `delegate_to_agent(target: "coder", task: "implement the login endpoint")`, the coder executes and returns its result to the lead, who continues its own reasoning.

## Supervisor Architecture

Build a supervisor that coordinates multiple workers:

```csharp
var workflow = Spectra.Workflow("supervised")
    .AddAgentNode("supervisor", agent: "planner")
    .AddAgentNode("frontend", agent: "frontend-dev")
    .AddAgentNode("backend", agent: "backend-dev")
    .AddAgentNode("reviewer", agent: "code-reviewer")
    .Edge("supervisor", "frontend", condition: "state.task_type == 'frontend'")
    .Edge("supervisor", "backend", condition: "state.task_type == 'backend'")
    .Edge("frontend", "reviewer")
    .Edge("backend", "reviewer")
    .Edge("reviewer", "supervisor", condition: "state.review_status == 'needs_changes'")
    .Build();
```

## Agent Builder (Code-First)

The `AgentNodeBuilder` provides a fluent API for defining agent nodes:

```csharp
var workflow = Spectra.Workflow("multi-agent")
    .AddAgentNode("analyst", builder => builder
        .WithProvider("openai")
        .WithModel("gpt-4o")
        .WithSystemPrompt("Analyze the data and produce insights.")
        .WithMaxIterations(10)
        .WithTools("query_database", "delegate_to_agent"))
    .Build();
```

## Handoff Events

Every handoff emits events for observability:

- `HandoffInitiated` — Agent A requests handoff to Agent B
- `HandoffCompleted` — Agent B finishes and returns (delegation) or takes over (transfer)
- `HandoffFailed` — Target agent is not registered or execution fails

## Conversation Scope

Control how conversation history flows between agents:

```csharp
builder.AddAgent("coder", agent => agent
    .WithConversationScope(ConversationScope.Isolated)  // fresh context
    // or
    .WithConversationScope(ConversationScope.Inherited) // sees parent's history
);
```
