# AgentWithMcp

An autonomous agent that connects to an **MCP server** (Model Context Protocol) and uses its tools to read meeting notes from disk, then answers questions about decisions, action items, and follow-ups.

## What it demonstrates

- **MCP integration** — `AddMcpServer(...)` launches a subprocess, handshakes via JSON-RPC, discovers tools, and registers them as native `ITool` instances
- **Stdio transport** — the MCP server runs as a child process communicating over stdin/stdout
- **Tool namespace** — MCP tools are registered as `mcp__<server>__<tool>` (e.g., `mcp__filesystem__read_file`), keeping them distinct from native tools
- **AllowedTools whitelist** — only `read_file`, `list_directory`, and `get_file_info` are exposed to the agent; write tools are filtered out during discovery
- **InheritEnvironment** — the child process inherits `PATH` so `npx` resolves correctly (set to `false` in production with explicit env vars)
- **Transparent abstraction** — the workflow references MCP tools by name exactly like native tools; `AgentStep` doesn't know they come from an external process
- **Parallel tool execution** — when the agent calls `read_file` on all three meeting notes, Spectra executes them concurrently
- **Error recovery** — the agent handles access-denied errors and self-corrects the file path across iterations

## The scenario

Three meeting notes live in a `workflows/` directory (seeded at startup):

| File | Meeting |
|------|---------|
| `2024-11-05-product-roadmap.txt` | Q1 roadmap, API rate limiting, design system |
| `2024-11-12-backend-sync.txt` | Redis implementation, deployment pipeline, security review |
| `2024-11-19-sprint-retrospective.txt` | Sprint retro, process improvements, Sprint 23 goals |

The default question — *"What are all the open action items across every meeting, and who owns each one?"* — forces the agent to discover files, read all three, and synthesize a cross-meeting answer grouped by owner.

## Prerequisites

- **Node.js 18+** — `npx` must be on your PATH (the MCP server is fetched automatically on first run)
- **OpenRouter API key**

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## Run it

```bash
cd samples/AgentWithMcp
dotnet run
```

## Expected output

The agent makes ~5 iterations:

1. **Iteration 1** — calls `list_directory(".")` → access denied (sandboxed to `workflows/`)
2. **Iteration 2** — calls `list_directory("/workflows")` → access denied (relative path)
3. **Iteration 3** — calls `list_directory("<full_path>/workflows")` → discovers 3 files
4. **Iteration 4** — calls `read_file` on all 3 files **in parallel**
5. **Iteration 5** — synthesizes the final answer: 13 action items grouped by 6 owners with due dates and source meeting references

The exact iteration count depends on the model — some models get the path right on the first try.

## Key outputs

| Output | Description |
|--------|-------------|
| `response` | The agent's final synthesized answer |
| `iterations` | Number of LLM↔tool round trips taken |
| `stopReason` | `"stop"` (finished naturally) or `"max_iterations"` (hit the cap) |
| `messages` | Full conversation history including all tool calls and results |

## Try other questions

Uncomment any of these in `Program.cs`:

```csharp
"What was decided about the API rate limiting?"
"Summarise the product roadmap meeting in one paragraph."
"Which meetings did Alice attend?"
"List every decision made in November 2024."
```

## MCP vs native tools

The workflow code is identical to the [SingleAgent](../SingleAgent/) sample — the only difference is *where the tools come from*:

| | SingleAgent | AgentWithMcp |
|---|---|---|
| Tool source | `spectra.AddTool(...)` | `spectra.AddMcpServer(...)` |
| Registration | At build time | At host startup (async discovery) |
| Tool names | `calculator`, `clock` | `mcp__filesystem__read_file` |
| Requires | Nothing | Node.js (`npx`) |
| Workflow code | Identical | Identical |

## Architecture

```
Program.cs
    │
    ├─ AddMcpServer(McpServerConfig)
    │      │
    │      ▼
    │  SpectraHostedService.StartAsync()
    │      │
    │      ├─ StdioMcpTransport.StartAsync()     ← spawns: npx -y @modelcontextprotocol/server-filesystem
    │      ├─ McpClient.InitializeAsync()         ← JSON-RPC: initialize → tools/list
    │      ├─ McpToolProvider filters tools        ← AllowedTools whitelist applied
    │      └─ McpToolAdapter registered as ITool   ← mcp__filesystem__read_file etc.
    │
    ├─ WorkflowBuilder.Create("meeting-assistant")
    │      └─ .WithParameter("tools", ["mcp__filesystem__read_file", ...])
    │
    └─ runner.RunAsync(workflow, state)
           └─ AgentStep resolves tools from IToolRegistry
                  └─ MCP tools are indistinguishable from native tools
```