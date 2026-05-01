# Tools & MCP

Tools are functions that agents can call during their execution loop. Spectra provides a tool system with auto-discovery, a registry, and native MCP (Model Context Protocol) integration.

## The ITool Contract

```csharp
public interface ITool
{
    string Name { get; }
    ToolDefinition Definition { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct = default);
}

public class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public List<ToolParameter> Parameters { get; set; } = [];
}
```

## Writing a Custom Tool

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
            new ToolParameter { Name = "city", Type = "string", Description = "City name", Required = true }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct)
    {
        var city = arguments["city"]?.ToString()!;
        var weather = await FetchWeatherAsync(city, ct);
        return ToolResult.Ok(weather);
    }
}
```

## Tool Registration

### Manual Registration

```csharp
services.AddSpectra(builder =>
{
    builder.AddTool(new WeatherTool());
    builder.AddTool(new DatabaseQueryTool());
});
```

### Auto-Discovery with Attributes

```csharp
[SpectraTool]
public class SearchDocsTool : ITool { ... }
```

```csharp
builder.AddToolsFromAssembly(typeof(SearchDocsTool).Assembly);
```

Spectra scans the assembly for classes marked with `[SpectraTool]` and registers them automatically.

## Tool Resilience

All registered tools are automatically wrapped with circuit breaker protection via `ResilientToolDecorator`. Configure the policy via `ToolResilienceOptions`:

```csharp
// ToolResilienceOptions controls circuit breaker behaviour
public record ToolResilienceOptions
{
    public int FailureThreshold { get; init; } = 5;
    public TimeSpan CooldownPeriod { get; init; } = TimeSpan.FromSeconds(60);
    public int HalfOpenMaxAttempts { get; init; } = 1;
    public int SuccessThresholdToClose { get; init; } = 1;
    public Dictionary<string, string> FallbackTools { get; init; } = new();
}
```

The `DefaultToolResiliencePolicy` provides sensible defaults and is applied globally to all tools in the registry.

## MCP Integration

Spectra can connect to any [MCP server](https://modelcontextprotocol.io/) and use its tools as native Spectra tools.

### Stdio Transport

```csharp
builder.AddMcpServer("filesystem", mcp => mcp
    .UseStdio("npx", "-y", "@modelcontextprotocol/server-filesystem", "/path/to/files"));
```

### SSE Transport

```csharp
builder.AddMcpServer("remote-tools", mcp => mcp
    .UseSse("https://my-mcp-server.com/sse"));
```

### How It Works

1. `McpClient` connects to the MCP server via the configured transport
2. `McpToolProvider` discovers available tools via `tools/list`
3. `McpToolAdapter` wraps each MCP tool as an `ITool`
4. Tools are registered in the `IToolRegistry` and available to agents

### MCP Server Config Builder

Use the fluent overload for advanced configuration:

```csharp
builder.AddMcpServer("data-tools", mcp => mcp
    .UseStdio("node", "server.js")
    .WithEnvironment("API_KEY", apiKey)
    .WithResilience(new McpResilienceOptions
    {
        MaxRetries = 2,
        Timeout = TimeSpan.FromSeconds(10)
    }));
```

### Per-Agent MCP Servers

MCP servers are registered globally at the `SpectraBuilder` level. Once registered, any agent can reference their tools by name via `WithTools(...)` on the agent node.

## Built-in Tools

| Tool | Description |
|------|-------------|
| `TransferToAgentTool` | Transfer control to another agent |
| `DelegateToAgentTool` | Delegate a subtask to another agent |
| `RecallMemoryTool` | Query long-term memory |
| `StoreMemoryTool` | Save to long-term memory |
