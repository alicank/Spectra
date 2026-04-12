---
description: "Expose Spectra workflows as HTTP endpoints inside your ASP.NET Core application."
---

# ASP.NET Core Integration

`Spectra.AspNetCore` exposes workflows as HTTP endpoints inside your ASP.NET Core app.

It is not a standalone server.

That means Spectra runs behind your existing:

- authentication
- authorization
- CORS
- rate limiting
- logging
- middleware pipeline

If you already know `app.MapHealthChecks()` or `app.MapOpenApi()`, the model is similar:

```csharp
app.MapSpectra();
```

---

## Install

```bash
dotnet add package Spectra.AspNetCore
```

Then map Spectra in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSpectra(spectra =>
{
    spectra.AddOpenRouter(config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;
        config.DefaultModel = "openai/gpt-4o-mini";
    });

    spectra.AddWorkflowsFromDirectory("./workflows");
});

var app = builder.Build();

app.MapSpectra();

app.Run();
```

That is enough to expose the built-in HTTP endpoints.

---

## What `MapSpectra()` adds

By default, `MapSpectra()` registers endpoints under the `/spectra` prefix.

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/spectra/run` | Run a workflow and return the final result |
| `GET` | `/spectra/stream` | Run a workflow and stream events with SSE |
| `GET` | `/spectra/checkpoints/{runId}` | Get checkpoint status for a run |
| `POST` | `/spectra/interrupt/{runId}` | Resume a paused workflow with an interrupt response |
| `POST` | `/spectra/fork/{runId}` | Fork a run from a checkpoint |

---

## Run a workflow

### `POST /spectra/run`

Use this when you want to execute a workflow and wait for the final result.

**Request**

```json
{
  "workflowId": "my-workflow",
  "inputs": {
    "text": "Summarize this document...",
    "language": "en"
  }
}
```

**Response**

```json
{
  "runId": "run_abc123",
  "workflowId": "my-workflow",
  "success": true,
  "errors": [],
  "artifacts": {},
  "context": {
    "nodes.summarize.output.response": "The document covers..."
  },
  "currentNodeId": null
}
```

You can also send a full `WorkflowDefinition` inline instead of only `workflowId`.

If both are provided, the inline workflow takes precedence.

---

## Stream a workflow

### `GET /spectra/stream`

Use this when you want live execution updates or token streaming.

Example:

```text
GET /spectra/stream?workflowId=my-workflow&inputs[text]=Hello
```

This endpoint streams workflow events as Server-Sent Events (SSE).

Typical uses:

- live progress UIs
- chat-style responses
- step-by-step workflow status

See [Streaming](execution/streaming.md).

---

## Check checkpoint state

### `GET /spectra/checkpoints/{runId}`

Use this to inspect the current checkpoint status of a run.

**Response**

```json
{
  "runId": "run_abc123",
  "workflowId": "my-workflow",
  "status": "AwaitingInput",
  "stepsCompleted": 3,
  "lastCompletedNodeId": "gather-info",
  "nextNodeId": "process",
  "updatedAt": "2025-04-01T14:32:00Z"
}
```

This is useful for:

- polling long-running workflows
- checking whether a run is paused
- inspecting progress without streaming

---

## Resume after an interrupt

### `POST /spectra/interrupt/{runId}`

Use this when a workflow is paused and waiting for approval or another interrupt response.

**Request**

```json
{
  "workflowId": "loan-application",
  "approved": true,
  "respondedBy": "jane.doe@company.com",
  "comment": "Looks good, approved.",
  "data": {
    "adjustedLimit": 50000
  }
}
```

This endpoint is commonly used for:

- approval workflows
- human-in-the-loop review
- multi-agent approval gates

See [Interrupts](execution/interrupts.md).

---

## Fork a run

### `POST /spectra/fork/{runId}`

Use this to branch from an earlier checkpoint into a new run.

**Request**

```json
{
  "workflowId": "my-workflow",
  "checkpointIndex": 2,
  "newRunId": "run_forked_xyz"
}
```

This is useful for:

- replaying from an earlier point
- testing alternative inputs
- sandboxing production runs
- debugging without changing the original run

See [Time Travel](execution/time-travel.md).

---

## How workflows are resolved

When the HTTP API receives a `workflowId`, Spectra looks it up through `IWorkflowStore`.

The easiest setup is to load workflows from a directory:

```csharp
spectra.AddWorkflowsFromDirectory("./workflows");
```

That loads workflow files at startup and makes them available by ID.

You can also provide a full workflow definition inline in the request body.

Use that when workflows are:

- generated dynamically
- stored outside the app
- managed by another system

---

## Change the route prefix

The default prefix is `/spectra`.

You can change it:

```csharp
app.MapSpectra("/api/workflows");
```

That gives you endpoints like:

- `POST /api/workflows/run`
- `GET /api/workflows/stream`

and so on.

---

## Auth and middleware

`MapSpectra()` returns an `IEndpointConventionBuilder`, so you can apply normal ASP.NET Core endpoint configuration.

```csharp
app.MapSpectra()
   .RequireAuthorization("WorkflowsPolicy")
   .RequireCors("AllowFrontend")
   .WithTags("Workflows");
```

This is one of the main benefits of the ASP.NET Core integration:

- Spectra does not replace your hosting model
- Spectra fits into it

### Production checklist

- require authentication on all Spectra endpoints
- restrict `fork` and `interrupt` endpoints to trusted roles
- apply rate limiting to workflow execution endpoints
- treat workflow execution like any other privileged application API

---

## A simple mental model

`Spectra.AspNetCore` turns workflow execution into normal ASP.NET Core endpoints.

- `AddSpectra(...)` configures workflows and runtime services
- `MapSpectra()` exposes them over HTTP
- your application still owns hosting, security, and middleware

That is the core model.

---

## What's next?

<div class="grid cards" markdown>

- **Workflow Runner**

  Learn how execution works under the hood.

  [:octicons-arrow-right-24: Runner](execution/runner.md)

- **Streaming**

  Stream workflow events and token output over SSE.

  [:octicons-arrow-right-24: Streaming](execution/streaming.md)

- **Interrupts**

  Pause for approval and resume execution safely.

  [:octicons-arrow-right-24: Interrupts](execution/interrupts.md)

- **Time Travel**

  Replay or fork from earlier checkpoints.

  [:octicons-arrow-right-24: Time Travel](execution/time-travel.md)

</div>