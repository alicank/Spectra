# Tools & MCP

Tools are functions that agents can call during their execution loop. Spectra provides a tool system with auto-discovery, a registry, and native MCP (Model Context Protocol) integration.

## The ITool Contract

```csharp
public interface ITool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default);
}

public class ToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public List<ToolParameter> Parameters { get; }
}
```

## Writing a Custom Tool

```csharp
public class WeatherTool : ITool
{
    public ToolDefinition Definition => new()
    {
        Name = "get_weather",
        Description = "Get current weather for a city",
        Parameters = new()
        {
            new ToolParameter("city", "string", "City name", required: true)
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        var city = parameters["city"].ToString()!;
        var weather = await FetchWeatherAsync(city, ct);
        return ToolResult.Success(weather);
    }
}
```

## Tool Registration

### Manual Registration

```csharp
services.AddSpectra(builder =>
{
    builder.AddTool<WeatherTool>();
    builder.AddTool<DatabaseQueryTool>();
});
```

### Auto-Discovery with Attributes

```csharp
[SpectraTool("search_docs", "Search the documentation for a query")]
public class SearchDocsTool : ITool { ... }
```

```csharp
builder.AddToolsFromAssembly(typeof(SearchDocsTool).Assembly);
```

The `ToolDiscovery` service scans assemblies for classes with `[SpectraTool]` and registers them automatically.

## Tool Resilience

Wrap tools with retry, timeout, and circuit breaker policies:

```csharp
builder.AddTool<ExternalApiTool>(resilience: new ToolResilienceOptions
{
    MaxRetries = 3,
    Timeout = TimeSpan.FromSeconds(30),
    CircuitBreakerThreshold = 5
});
```

The `ResilientToolDecorator` wraps any tool with Polly-style resilience. The `DefaultToolResiliencePolicy` provides sensible defaults.

## MCP Integration

Spectra can connect to any [MCP server](https://modelcontextprotocol.io/) and use its tools as native Spectra tools.

### Stdio Transport

```csharp
builder.AddMcpServer("filesystem", new McpServerConfig
{
    Command = "npx",
    Args = new[] { "-y", "@modelcontextprotocol/server-filesystem", "/path/to/files" },
    Transport = "stdio"
});
```

### SSE Transport

```csharp
builder.AddMcpServer("remote-tools", new McpServerConfig
{
    Url = "https://my-mcp-server.com/sse",
    Transport = "sse"
});
```

### How It Works

1. `McpClient` connects to the MCP server via the configured transport
2. `McpToolProvider` discovers available tools via `tools/list`
3. `McpToolAdapter` wraps each MCP tool as an `ITool`
4. Tools are registered in the `IToolRegistry` and available to agents

### Per-Agent MCP Servers

Different agents can have access to different MCP servers:

```csharp
builder.AddAgent("file-agent", agent => agent
    .WithMcpServer("filesystem")
    .WithMcpServer("git"));

builder.AddAgent("data-agent", agent => agent
    .WithMcpServer("database")
    .WithMcpServer("analytics"));
```

### MCP Server Config Builder

```csharp
var config = McpServerConfig.Builder()
    .WithCommand("node", "server.js")
    .WithEnvironment("API_KEY", apiKey)
    .WithResilience(new McpResilienceOptions
    {
        MaxRetries = 2,
        ConnectionTimeout = TimeSpan.FromSeconds(10)
    })
    .Build();
```

## Built-in Tools

| Tool | Description |
|------|-------------|
| `TransferToAgentTool` | Transfer control to another agent |
| `DelegateToAgentTool` | Delegate a subtask to another agent |
| `RecallMemoryTool` | Query long-term memory |
| `StoreMemoryTool` | Save to long-term memory |
