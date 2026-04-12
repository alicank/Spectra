# Execution Engine

The execution engine is Spectra's core loop. It takes a `WorkflowDefinition` and runs it step by step, resolving state, evaluating conditions, and handling parallelism.

## WorkflowRunner

The `WorkflowRunner` is the main entry point for executing workflows:

```csharp
var runner = services.GetRequiredService<IWorkflowRunner>();
var result = await runner.RunAsync(workflow, inputs, cancellationToken);
```

The runner handles:

- Topological ordering of nodes via `ExecutionPlan`
- Sequential execution of nodes in dependency order
- Parallel execution of independent branches via `ParallelScheduler`
- Condition evaluation on edges to determine branching
- Checkpoint saves at configurable points
- Resume from a checkpoint after interruption

## Execution Flow

```
Start
  │
  ▼
Build ExecutionPlan (topological sort)
  │
  ▼
┌─── Next node(s) ready? ───┐
│         │                  │
│    Single node        Multiple nodes
│         │                  │
│    Run sequentially   Run via ParallelScheduler
│         │                  │
│         ▼                  ▼
│    Execute step       Execute steps concurrently
│         │                  │
│    Write outputs      Merge outputs (with reducers)
│         │                  │
│    Evaluate edges     Evaluate edges
│         │                  │
│         └──────┬───────────┘
│                │
│         Checkpoint (if configured)
│                │
│                ▼
│         More nodes? ─── Yes ──▶ loop back
│                │
│               No
│                │
│                ▼
│            Complete
```

## ExecutionPlan

The `ExecutionPlan` performs topological sorting on the graph to determine a valid execution order. It also detects:

- Parallel branches (nodes with no dependencies between them)
- Cycles (and validates them against the `CyclePolicy`)
- Unreachable nodes

## ParallelScheduler

When the execution plan identifies independent branches, the `ParallelScheduler` handles concurrent execution:

```csharp
// Internal — you don't call this directly
var scheduler = new ParallelScheduler(maxConcurrency: 4);
await scheduler.ExecuteAsync(independentNodes, state, cancellationToken);
```

Concurrency is configurable. State writes from parallel branches use registered reducers to merge safely.

## RunContext

The `RunContext` carries run-level information through the execution:

```csharp
public class RunContext
{
    public string RunId { get; }
    public IServiceProvider Services { get; }
    public IEventSink EventSink { get; }
    public CancellationToken CancellationToken { get; }
    public CheckpointOptions? CheckpointOptions { get; }
}
```

Every step receives the `RunContext` through its `StepContext`, giving it access to DI services, the event sink for custom events, and cancellation.

## Error Handling

If a step throws an exception or returns `StepStatus.Failed`:

1. The error is captured in the step's output
2. A `StepFailed` event is emitted
3. The workflow stops (unless a fallback edge is defined)
4. The final `WorkflowResult` reflects the failure

You can define error-handling edges using conditions:

```csharp
.Edge("risky-step", "fallback", condition: "state.nodes.risky-step.status == 'Failed'")
```
