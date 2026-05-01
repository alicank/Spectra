![Spectra](icon.png)

# Spectra

**AI workflow orchestration for .NET.**
Build workflows as graphs, mix code and agent steps, swap providers without changing the flow.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/Spectra?label=NuGet&color=004880)](https://www.nuget.org/packages/Spectra)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Spectra?label=Downloads&color=green)](https://www.nuget.org/packages/Spectra)

[Documentation](https://alicank.github.io/Spectra/) · [Getting Started](https://alicank.github.io/Spectra/getting-started/) · [Samples](#samples) · [NuGet](https://www.nuget.org/packages/Spectra)

---

## Quickstart

```bash
dotnet new console -n MyWorkflow && cd MyWorkflow
dotnet add package Spectra
dotnet add package Spectra.Extensions
```

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(c =>
            {
                c.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;
                c.Model = "openai/gpt-4o-mini";
            });
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var workflow = WorkflowBuilder.Create("greet-and-translate")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", a => a
        .WithSystemPrompt("You are a helpful assistant."))
    .AddAgentNode("greet", "assistant", n => n
        .WithUserPrompt("Say hello to {{inputs.name}}.")
        .WithMaxIterations(1))
    .AddAgentNode("translate", "assistant", n => n
        .WithUserPrompt("Translate to French: {{nodes.greet.output}}")
        .WithMaxIterations(1))
    .AddEdge("greet", "translate")
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();
var state = new WorkflowState { ["inputs.name"] = "World" };
var result = await runner.RunAsync(workflow, state);
Console.WriteLine(result["nodes.translate.output"]);
```

```bash
dotnet run
```

Nodes do work. Edges define flow. State moves through the graph.

> **Using a different provider?** Replace `AddOpenRouter(...)` with `AddOpenAI(...)`, `AddAnthropic(...)`, `AddGemini(...)`, or `AddOllama(...)`. The workflow definition stays the same.

[**Read the full getting-started guide →**](https://alicank.github.io/Spectra/getting-started/)

---

## Why Spectra

**Workflows are visible graphs.** Define workflows as directed graphs in C# or JSON. Every step, edge, and condition is explicit — not buried in application code.

**Mix any kind of step.** Code functions, LLM prompts, autonomous agents, human approval gates, subgraphs — all first-class nodes in the same workflow.

**Swap providers freely.** Route each step to OpenAI, Claude, Gemini, Ollama, or OpenRouter. Define fallback chains. The workflow definition doesn't change.

**Define in C# or JSON.** Use the fluent builder for code-first control, or JSON for portable, editable definitions that don't require recompilation. Both describe the same model.

---

## Features

- **Graph-based orchestration** — directed graphs with conditional edges, parallel fan-out, and cyclic loops with guard rails
- **Multi-provider** — OpenAI, Anthropic, Gemini, Ollama, OpenRouter, and any OpenAI-compatible API
- **Agent step** — autonomous tool-using agents with iteration limits and cost tracking *(coming soon)*
- **Multi-agent** — supervisor, handoff, and delegation patterns
- **MCP integration** — connect agents to MCP tool servers over stdio or SSE
- **Checkpointing** — pause and resume workflows from any node
- **Time travel** — fork execution from any checkpoint and explore different paths
- **Interrupts** — pause any step for human approval, inject feedback, resume
- **Streaming** — token-level streaming through the workflow pipeline
- **Prompt management** — prompts as markdown files with YAML front matter and `{{variable}}` templating
- **Resilience** — provider fallback chains, tool circuit breakers, retry with backoff, response caching
- **Sessions** — multi-turn conversational state with history windowing
- **Memory** — cross-session persistent memory for agents
- **Observability** — OpenTelemetry tracing, structured events, compliance audit trail
- **Typed state** — compile-time merge policies for parallel execution
- **Standard DI** — `AddSpectra(...)` plugs into `IServiceCollection` like any .NET service

---

## Packages

| Package | Description |
|---------|-------------|
| [`Spectra`](https://www.nuget.org/packages/Spectra) | Entry point — DI registration, fluent builders, hosted service |
| [`Spectra.Extensions`](https://www.nuget.org/packages/Spectra.Extensions) | LLM providers: OpenAI, Anthropic, Gemini, Ollama, OpenRouter |
| [`Spectra.Kernel`](https://www.nuget.org/packages/Spectra.Kernel) | Execution engine, scheduler, built-in steps, resilience decorators |
| [`Spectra.Contracts`](https://www.nuget.org/packages/Spectra.Contracts) | Interfaces and data models only — for building extensions |
| [`Spectra.AspNetCore`](https://www.nuget.org/packages/Spectra.AspNetCore) | HTTP endpoints for exposing workflows via ASP.NET Core |

Most projects need `Spectra` + `Spectra.Extensions`. The rest are for advanced scenarios.

---

## Samples

| Sample | What it shows |
|--------|--------------|
| [HelloWorld](samples/HelloWorld) | Minimal single-node workflow |
| [JsonVsCode](samples/JsonVsCode) | Same workflow defined in C# and JSON |
| [BranchingWorkflow](samples/BranchingWorkflow) | Conditional edges and dynamic routing |
| [ParallelFanOut](samples/ParallelFanOut) | Parallel branch execution with fan-in |
| [CyclicLoop](samples/CyclicLoop) | Retry loops with guard rails |
| [CheckpointResume](samples/CheckpointResume) | Pause and resume from checkpoint |
| [InterruptApproval](samples/InterruptApproval) | Human-in-the-loop approval gate |
| [SingleAgent](samples/SingleAgent) | Autonomous agent with tools |
| [AgentWithMcp](samples/AgentWithMcp) | Agent connected to MCP tool servers |
| [AgentWithFallback](samples/AgentWithFallback) | Provider fallback chains |
| [MultiAgent](samples/MultiAgent) | Agent-to-agent handoff |
| [MultiAgentSupervisor](samples/MultiAgentSupervisor) | Supervisor delegates to worker agents |
| [SubgraphComposition](samples/SubgraphComposition) | Nested workflows with isolated state |
| [StreamingOutput](samples/StreamingOutput) | Token-level streaming through the pipeline |
| [StructuredOutput](samples/StructuredOutput) | JSON-constrained LLM output |
| [MemoryStoreRecall](samples/MemoryStoreRecall) | Cross-session persistent memory |
| [ResearchPipeline](samples/ResearchPipeline) | Multi-step research with validation |

---

## Documentation

Full documentation at [**alicank.github.io/Spectra**](https://alicank.github.io/Spectra/).

- [Getting Started](https://alicank.github.io/Spectra/getting-started/) — install to running workflow in 60 seconds
- [Workflows & Graphs](https://alicank.github.io/Spectra/concepts/workflows/) — nodes, edges, state, branching, loops
- [LLM Providers](https://alicank.github.io/Spectra/llm/providers/) — setup for each provider
- [Agent Step](https://alicank.github.io/Spectra/llm/agent-step/) — autonomous tool-using agents
- [Multi-Agent Patterns](https://alicank.github.io/Spectra/multi-agent/overview/) — supervisor, handoff, delegation
- [Architecture](https://alicank.github.io/Spectra/architecture/) — package structure, execution flow, extension points

---

## Contributing

Contributions are welcome. Please open an issue first to discuss what you'd like to change.

```bash
git clone https://github.com/alicank/spectra.git
cd spectra
dotnet build
dotnet test
```

---

## License

[MIT](LICENSE)