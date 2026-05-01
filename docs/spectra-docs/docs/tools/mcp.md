---
description: "Connect MCP servers to Spectra and expose their tools as native agent tools."
---

# MCP Integration

Spectra can connect to any [Model Context Protocol](https://modelcontextprotocol.io/) server and expose its tools as normal Spectra tools.

That means an MCP server can provide capabilities like:

- filesystem access
- database queries
- API integrations
- external service actions

Once connected, agents use MCP tools by name just like any other tool.

---

## What MCP gives you

The main benefit is simple:

- Spectra connects to the MCP server
- Spectra discovers the server's tools
- each discovered tool is registered as a native `ITool`
- agents can whitelist and call those tools normally

After registration, your workflow does not need a separate tool model for MCP. The abstraction is the same.

---

## How it works

At startup, Spectra:

1. connects to each configured MCP server
2. discovers available tools
3. wraps them as native Spectra tools
4. registers them in `IToolRegistry`

From that point on, MCP tools behave like normal tools inside agent execution.

---

## Choose a transport

Spectra supports two common MCP transport modes:

| Transport | Use when |
| --- | --- |
| `stdio` | The MCP server runs as a local process |
| `sse` | The MCP server is available remotely over HTTP |

---

## Stdio transport

Use `stdio` when the MCP server runs on the same machine or is launched by your application.

```csharp
builder.AddMcpServer(new McpServerConfig
{
    Name = "filesystem",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/files"],
    Transport = McpTransportType.Stdio
});
```

In this mode:

- Spectra launches the process at startup
- communication happens over stdin/stdout
- tools are discovered automatically

This is a good default for local development and local tool servers.

---

## SSE transport

Use `sse` when the MCP server is already running remotely.

```csharp
builder.AddMcpServer(new McpServerConfig
{
    Name = "remote-tools",
    Url = "https://my-mcp-server.com/sse",
    Transport = McpTransportType.Sse
});
```

In this mode:

- Spectra connects to the remote endpoint
- the server streams events back over SSE
- tool calls are sent over HTTP

This is a good fit for shared or hosted MCP services.

---

## Fluent configuration

For more advanced setups, use the builder-style API.

```csharp
builder.AddMcpServer("data-tools", mcp => mcp
    .UseStdio("node", "server.js")
    .WithEnvironment("API_KEY", apiKey)
    .WithEnvironment("DATABASE_URL", dbUrl)
    .WithResilience(new McpResilienceOptions
    {
        MaxRetries = 2,
        Timeout = TimeSpan.FromSeconds(10)
    }));
```

This is useful when the server needs:

- environment variables
- custom launch arguments
- retry or timeout settings

---

## Main configuration fields

| Field | Description |
| --- | --- |
| `Name` | Identifier for the server |
| `Command` | Executable to launch for `stdio` |
| `Args` | Command-line arguments |
| `Url` | Endpoint for `sse` |
| `Transport` | `"stdio"` or `"sse"` |
| `EnvironmentVariables` | Environment variables for the launched process |

---

## Using MCP tools in agents

Once discovered, MCP tools are used like any other tool.

```csharp
builder.AddAgent("file-agent", "openai", "gpt-4o", agent => agent
    .WithSystemPrompt("You manage files in the project directory."));

workflow.AddAgentNode("manage-files", "file-agent", node => node
    .WithTools("read_file", "write_file", "list_directory")
    .WithUserPrompt("{{inputs.task}}"));
```

The agent does not need to know whether `read_file` came from:

- a native Spectra tool
- an MCP server

That distinction is hidden behind the tool abstraction.

---

## MCP resilience

MCP servers can fail or be temporarily unavailable, especially when they are remote.

You can configure retry and connection timeout behavior:

```csharp
var config = new McpServerConfig
{
    Name = "flaky-server",
    Url = "https://external-api.com/mcp",
    Transport = McpTransportType.Sse,
    Resilience = new McpResilienceOptions
    {
        MaxRetries = 3,
        Timeout = TimeSpan.FromSeconds(15)
    }
};
```

Use this when connecting to:

- remote services
- unstable networks
- external MCP providers

For per-tool protections such as circuit breakers, see [Tool Resilience](resilience.md).

---

## A simple mental model

An MCP server is just an external tool provider.

Spectra connects to it, discovers its tools, and makes those tools available to agents through the same tool system you already use.

That is the key idea.

---

## What's next?

<div class="grid cards" markdown>

- **Tools Overview**

  Learn the core tool model, custom tools, and tool registration.

  [:octicons-arrow-right-24: Tools](overview.md)

- **Tool Resilience**

  Add retry, timeout, and protection for unreliable tool endpoints.

  [:octicons-arrow-right-24: Tool Resilience](resilience.md)

</div>