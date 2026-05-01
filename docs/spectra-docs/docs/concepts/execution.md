# Execution Engine

The execution engine is Spectra's core loop. It takes a `WorkflowDefinition`, resolves node inputs from `WorkflowState`, executes steps, applies outputs, evaluates outgoing edges, emits events, and saves checkpoints when configured.

Spectra has two execution paths:

- `WorkflowRunner` — the main sequential runner used by `IWorkflowRunner`
- `ParallelScheduler` — a separate scheduler for fan-out/fan-in style parallel workflows

## WorkflowRunner

The `WorkflowRunner` is the main entry point for executing workflows:

```csharp
var runner = services.GetRequiredService<IWorkflowRunner>();
var result = await runner.RunAsync(workflow, initialState, cancellationToken);
```

The runner handles:

- structural validation before execution
- sequential execution starting at `EntryNodeId` or the first node
- input resolution through `IStateMapper`
- condition evaluation on outgoing edges
- loopback edges and `MaxNodeIterations`
- checkpoint saves at configured points
- resume, interrupt response, session messages, and forked execution
- workflow, step, state, branch, interrupt, streaming, and fork events

The sequential runner does not automatically switch into `ParallelScheduler`. If a workflow has multiple unconditional outgoing edges from a node, validation warns that the sequential runner follows only the first one.

## Execution Flow

```text
Start
  |
  v
Validate workflow
  |
  v
Create or restore WorkflowState
  |
  v
Select current node
  |
  v
Resolve step inputs
  |
  v
Handle interrupt-before if configured
  |
  v
Execute step
  |
  v
Handle StepStatus
  |
  +-- Failed / Interrupted / NeedsContinuation / AwaitingInput --> save checkpoint if configured, stop
  |
  +-- Succeeded / Handoff --> apply outputs
  |
  v
Handle interrupt-after if configured
  |
  v
Evaluate outgoing edges in order
  |
  v
Save checkpoint if configured
  |
  v
Next node? yes -> loop
  |
  no
  |
  v
Complete
```

## ExecutionPlan

`ExecutionPlan` belongs to the parallel scheduling path. It performs dependency resolution and topological sorting over non-loopback edges. It also records validation errors such as:

- duplicate node IDs
- edges that reference missing nodes
- an invalid entry node
- cycles among non-loopback edges

Loopback edges are excluded from topological sorting and cycle detection.

## ParallelScheduler

`ParallelScheduler` handles concurrent execution for workflows that need fan-out and fan-in behavior:

```csharp
var scheduler = services.GetRequiredService<ParallelScheduler>();
var result = await scheduler.ExecuteAsync(workflow, initialState, cancellationToken);
```

It builds an `ExecutionPlan`, executes ready nodes concurrently up to `maxConcurrency`, emits parallel batch events, evaluates incoming edge conditions before marking dependent nodes ready, and applies outputs with `StateMapper`.

Parallel state writes are protected by a lock while outputs are applied. Use output mappings carefully when multiple branches write to shared locations.

## RunContext

`RunContext` carries caller-supplied identity and tenant information through the execution:

```csharp
public class RunContext
{
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public IEnumerable<Claim> Claims { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
    public IEnumerable<string> Roles { get; set; }
}
```

Pass it with the overload that accepts a `RunContext`:

```csharp
var runContext = new RunContext
{
    TenantId = "tenant-1",
    UserId = "user-42",
    CorrelationId = "request-123"
};

var result = await runner.RunAsync(workflow, initialState, runContext, cancellationToken);
```

Every step receives this through `StepContext.RunContext`. `StepContext` also carries services, workflow state, resolved inputs, cancellation, memory, the current workflow definition, and optional interrupt and token-streaming callbacks.

## Error Handling

If a step returns `StepStatus.Failed`:

1. A `StepCompletedEvent` is emitted with `Status = StepStatus.Failed`.
2. The error message is added to `WorkflowState.Errors`.
3. A failure checkpoint is saved if checkpointing is configured for failures.
4. The workflow stops and the final `WorkflowState.Status` is `WorkflowRunStatus.Failed`.

If a step throws `InterruptException`, the runner emits `StepInterruptedEvent`, saves an interrupt checkpoint if configured, and stops in the interrupted state.

General exceptions from step code are not converted into fallback branches by the sequential runner. If you need a fallback path, return a successful/recoverable output such as `status = NeedsFallback` and route with a conditional edge, or use resilience/fallback support around the operation:

```csharp
.AddEdge("risky-step", "fallback", condition: "nodes.risky-step.status == 'NeedsFallback'")
```
