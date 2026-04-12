---
description: "Run Spectra in background workers or console apps using the standard .NET hosting model."
---

# Worker & Console Host

Spectra runs inside any .NET host that supports `IHostedService`.

In practice, that means the same Spectra setup works in:

- background workers
- console apps
- ASP.NET Core apps
- other hosted .NET processes

This page covers two common non-HTTP patterns:

- a **background worker** for long-running or triggered workflows
- a **console app** for one-shot runs and scripts

The Spectra registration is the same in both cases. Only the host pattern around it changes.

---

## Background worker

Use a worker service when workflows should run continuously in response to external work.

Typical cases:

- queue consumers
- scheduled jobs
- batch processing
- event-driven orchestration without HTTP

### Setup

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(c =>
            {
                c.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;
                c.DefaultModel = "openai/gpt-4o-mini";
            });

            spectra.AddWorkflowsFromDirectory("./workflows");
            spectra.AddFileCheckpoints("./checkpoints");
            spectra.AddConsoleEvents();
        });

        services.AddHostedService<DocumentProcessingWorker>();
    })
    .Build();

await host.RunAsync();
```

### Worker example

```csharp
public class DocumentProcessingWorker : BackgroundService
{
    private readonly IWorkflowRunner _runner;
    private readonly IWorkflowStore _workflowStore;

    public DocumentProcessingWorker(IWorkflowRunner runner, IWorkflowStore workflowStore)
    {
        _runner = runner;
        _workflowStore = workflowStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var document = await DequeueNextDocumentAsync(stoppingToken);
            if (document is null)
                continue;

            var workflow = _workflowStore.Get("process-document")!;

            var state = new WorkflowState();
            state["inputs.documentId"] = document.Id;
            state["inputs.content"] = document.Content;

            await _runner.RunAsync(workflow, state, stoppingToken);
        }
    }
}
```

The worker just resolves `IWorkflowRunner` and `IWorkflowStore` from DI and runs workflows as needed.

### Graceful shutdown

`RunAsync(...)` accepts a `CancellationToken`.

When the host shuts down:

- the token is cancelled
- the current workflow execution is asked to stop cleanly
- if checkpointing is enabled, the run can resume later

Without checkpointing, interrupted runs start over on the next launch.

---

## Console app

Use a console app when you want a one-shot workflow run.

Typical cases:

- scripts
- local tools
- development testing
- one-time batch jobs
- migrations or exports

### Example

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddAnthropic(c =>
            {
                c.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!;
                c.DefaultModel = "claude-sonnet-4-20250514";
            });

            spectra.AddConsoleEvents();
        });
    })
    .Build();

await host.StartAsync();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var workflow = WorkflowBuilder.Create("summarize")
    .AddAgent("assistant", "anthropic", "claude-sonnet-4-20250514")
    .AddAgentNode("summarize", "assistant", node => node
        .WithUserPrompt("Summarize this report: {{inputs.text}}")
        .WithMaxIterations(1))
    .Build();

var state = new WorkflowState();
state["inputs.text"] = File.ReadAllText(args[0]);

var result = await runner.RunAsync(workflow, state);

Console.WriteLine(result["nodes.summarize.output.response"]);

await host.StopAsync();
```

### Why `StartAsync()` matters

In console apps, call:

```csharp
await host.StartAsync();
```

before resolving and using the runner.

That ensures `SpectraHostedService` has already finished startup work such as:

- tool discovery
- MCP connections
- agent tool validation

Without this, some runtime features may not be ready yet.

---

## Startup lifecycle

The startup flow is the same in worker and console hosts:

```text
Host.StartAsync()
  -> SpectraHostedService.StartAsync()
     -> discover tools
     -> connect MCP servers
     -> validate agent tool declarations
  -> your workflow execution begins
```

If a required MCP server fails during startup, host startup fails.

That is usually the right behavior, because it surfaces configuration problems early instead of causing harder-to-debug failures later during workflow execution.

---

## Which host should you use?

| | Background worker | Console app |
| --- | --- | --- |
| Lifetime | Long-running | One-shot |
| Trigger | Queue, timer, external event | Command line |
| Concurrency | Often many runs | Usually one run |
| Best for | Production jobs and pipelines | Scripts and local tools |
| Startup pattern | `RunAsync()` | `StartAsync()` / `StopAsync()` |

A simple rule:

- choose a **worker** when workflows are part of an ongoing service
- choose a **console app** when workflows are part of a single command

---

## A simple mental model

Spectra does not require a special host.

It plugs into the normal .NET hosting model:

- register Spectra with DI
- start the host
- resolve the runner
- execute workflows

The rest depends on whether your app is long-running or one-shot.

---

## What's next?

<div class="grid cards" markdown>

- **ASP.NET Core**

  Expose workflows over HTTP.

  [:octicons-arrow-right-24: ASP.NET Core](aspnetcore.md)

- **Checkpointing**

  Persist workflow state for clean resume.

  [:octicons-arrow-right-24: Checkpointing](../execution/checkpointing.md)

- **Tools Overview**

  Register tools and MCP servers for agents.

  [:octicons-arrow-right-24: Tools](../tools/overview.md)

</div>