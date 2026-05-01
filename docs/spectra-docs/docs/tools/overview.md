---
description: "Create, register, and use tools in Spectra so agents can search, fetch data, call APIs, and coordinate with other agents."
---

# Tools Overview

Tools are functions that agents can call during their reasoning loop.

Without tools, an agent can generate text.

With tools, it can:

- search
- fetch data
- call APIs
- write files
- query systems
- coordinate with other agents

In Spectra, tools are how agents interact with the outside world.

---

## How tools fit into Spectra

The common flow is:

1. you implement a tool
2. you register it in Spectra
3. an agent is given access to it
4. the model decides when to call it

This lets the agent combine reasoning with action.

---

## Writing a custom tool

Every tool implements `ITool`.

```csharp
public interface ITool
{
    string Name { get; }
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default);
}
```

- `Name` is the tool name exposed to the model
- `Definition` describes what the tool does and which parameters it accepts
- `ExecuteAsync(...)` runs the actual logic

### Example

```csharp
public class WeatherTool : ITool
{
    public string Name => "get_weather";

    public ToolDefinition Definition => new()
    {
        Name = "get_weather",
        Description = "Get current weather for a city",
        Parameters =
        [
            new ToolParameter
            {
                Name = "city",
                Type = "string",
                Description = "City name (e.g. 'Paris', 'Tokyo')",
                Required = true
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        var city = arguments["city"]?.ToString();
        if (string.IsNullOrEmpty(city))
            return ToolResult.Fail("City is required.");

        var weather = await FetchWeatherAsync(city, ct);
        return ToolResult.Ok(weather);
    }
}
```

This is the basic pattern for most custom tools.

---

## Tool definitions and results

A tool definition tells the model how to call the tool.

```csharp
public class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public List<ToolParameter> Parameters { get; set; } = [];
}

public class ToolParameter
{
    public required string Name { get; set; }
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool Required { get; set; }
}
```

A tool returns a `ToolResult`:

```csharp
ToolResult.Ok("The weather in Paris is 22°C and sunny.");
ToolResult.Fail("City not found.");
```

Use successful results for normal output and failed results when the tool cannot complete the request.

---

## Registering tools

### Manual registration

```csharp
services.AddSpectra(builder =>
{
    builder.AddTool(new WeatherTool());
    builder.AddTool(new DatabaseQueryTool());

    // Or register several at once
    builder.AddTools(new SearchTool(), new CalculatorTool());
});
```

### Auto-discovery

You can also decorate tools and scan assemblies at startup.

```csharp
[SpectraTool]
public class SearchDocsTool : ITool
{
    // ...
}
```

```csharp
builder.AddToolsFromAssembly(typeof(SearchDocsTool).Assembly);
// or
builder.AddToolsFromAssembly<SearchDocsTool>();
```

This is useful when you want to organize tools across projects or packages without registering each one manually.

---

## How agents get tools

Agents can receive tools from several sources.

### Explicit tool list

The most common pattern is to whitelist tools on the node or agent.

```csharp
.AddAgentNode("research", "researcher", node => node
    .WithTools("web_search", "read_url"))
```

If a tool name is unknown, the step fails immediately.

### Auto-injected tools

Spectra can also inject built-in tools automatically depending on agent and session configuration.

These include:

- handoff tools
- delegation tools
- memory tools
- session-ending tools

---

## Parallel tool execution

If the model returns multiple tool calls in one turn, Spectra executes them concurrently.

That means an agent can call several tools in the same iteration without waiting for each one sequentially.

This is especially useful for patterns like:

- multiple searches
- parallel retrieval
- checking several sources at once

---

## Built-in tools

Spectra provides several built-in tools that are injected automatically when needed.

| Tool | Injected when | Behavior |
| --- | --- | --- |
| `transfer_to_agent` | Agent has handoff targets | Intercepted by `AgentStep` and triggers a handoff |
| `delegate_to_agent` | Agent is a supervisor | Executes a nested agent run and returns the worker result |
| `recall_memory` | Memory is configured and auto-injection is enabled | Reads from long-term memory |
| `store_memory` | Memory is configured and auto-injection is enabled | Writes to long-term memory |
| `end_session` | `SessionStep` uses `LlmDecides` exit policy | Intercepted and ends the session |

These tools are part of Spectra's agent runtime. You do not usually implement them yourself.

---

## When to build a custom tool

Create a custom tool when an agent needs to do something your workflow cannot express with prompting alone.

Good examples:

- call an internal API
- search your own knowledge base
- fetch customer data
- write to a ticketing system
- trigger an external action

A useful rule of thumb:

- use prompts for reasoning
- use tools for actions and external data

---

## A simple mental model

A tool is just:

- a name
- a schema the model can understand
- code that executes the action
- a result returned back into the agent loop

That is the core abstraction.

---

## What's next?

<div class="grid cards" markdown>

- **MCP Integration**

  Connect external tool servers through the Model Context Protocol.

  [:octicons-arrow-right-24: MCP](mcp.md)

- **Tool Resilience**

  Add retries, fallbacks, and safety around unreliable tools.

  [:octicons-arrow-right-24: Tool Resilience](resilience.md)

- **Agent Step**

  See how agents call tools inside the reasoning loop.

  [:octicons-arrow-right-24: Agent Step](../llm/agent-step.md)

</div>