# Hosting Spectra

Spectra is a NuGet package, not a server. It embeds inside whatever .NET host you're already running — an ASP.NET Core web app, a background worker, a console tool, a microservice. You bring the host; Spectra brings the workflow engine.

There are two common patterns:

| Pattern | Host type | Use when |
|---------|-----------|----------|
| [ASP.NET Core](aspnetcore.md) | Web app / API | You want to expose workflows over HTTP, with streaming, interrupts, and checkpoints accessible via REST. |
| [Worker / Console](worker.md) | `IHostedService` or console app | You want to run workflows from background jobs, scripts, scheduled tasks, or a custom CLI. |

Both patterns use the same `AddSpectra(...)` registration call. The host shell is the only thing that changes.

---

## What Happens at Startup

When your host starts, `AddSpectra` registers the Spectra runtime into the .NET DI container — all registries, the workflow runner, and the parallel scheduler. It also registers a `SpectraHostedService` that runs automatically and does three things:

**1. Tool auto-discovery**

If you called `AddToolsFromAssembly(...)`, the hosted service scans those assemblies at startup and registers every class decorated with `[SpectraTool]` into the tool registry. You don't need to enumerate them manually.

**2. MCP server connections**

If you called `AddMcpServer(...)`, the hosted service opens and initializes those connections before the first workflow runs. If a connection fails, startup fails — by design. A workflow that references a missing MCP tool should never silently degrade.

**3. Agent tool validation**

After tools are registered, the hosted service checks every global agent definition. If an agent declares a tool that isn't in the registry, it logs a warning with the agent ID and tool name. This catches misconfiguration early — at startup — rather than at runtime when a workflow is actually executing.

This means by the time your first request arrives (or your first workflow runs), all tools are registered, all MCP connections are open, and any misconfiguration has already been flagged.

---

## The Registration API

`AddSpectra` accepts a fluent builder that covers everything the runtime needs:

```csharp
services.AddSpectra(spectra =>
{
    // LLM providers
    spectra.AddOpenRouter(c => { c.ApiKey = "..."; });
    spectra.AddAnthropic(c => { c.ApiKey = "..."; });

    // Global agents
    spectra.AddAgent("analyst", "anthropic", "claude-sonnet-4-20250514", agent => agent
        .WithSystemPrompt("You are a data analyst.")
        .WithTools("query_database", "generate_chart"));

    // Tools
    spectra.AddToolsFromAssembly<MyTools>();
    spectra.AddMcpServer("filesystem", config =>
        config.UseStdio("npx", "-y @modelcontextprotocol/server-filesystem"));

    // Persistence
    spectra.AddFileCheckpoints("./checkpoints");
    spectra.AddFileMemory("./memory");

    // Workflows
    spectra.AddWorkflowsFromDirectory("./workflows");

    // Observability
    spectra.AddConsoleEvents();
});
```

Everything registered here is available to all workflows running in that host. Agents, tools, providers, checkpoints — all shared across runs.

---

## Choosing a Pattern

**Use ASP.NET Core** if:
- Users or external systems trigger workflow runs via HTTP
- You need streaming responses (Server-Sent Events)
- You want interrupt/resume accessible via API calls
- You're building a service that other services call

**Use Worker / Console** if:
- Workflows run on a schedule or in response to queue messages
- You're processing batches or pipelines without a human in the loop
- You want a lightweight process with no HTTP overhead
- You're building an internal tool or script

The two patterns aren't mutually exclusive. A single host can run background worker workflows *and* expose HTTP endpoints for triggering or monitoring them.

---

## What's Next

<div class="grid cards" markdown>

-   **ASP.NET Core**

    HTTP endpoints, streaming, interrupts over REST.

    [:octicons-arrow-right-24: ASP.NET Core](aspnetcore.md)

-   **Worker & Console**

    Background services, scheduled jobs, console apps.

    [:octicons-arrow-right-24: Worker & Console](worker.md)

</div>