# Events & Observability

Spectra emits structured events at every stage of workflow execution. This is how you observe, log, debug, and trace what's happening.

## Event System

All events flow through `IEventSink`:

```csharp
public interface IEventSink
{
    Task EmitAsync(WorkflowEvent evt, CancellationToken ct = default);
}
```

Events are emitted for workflow lifecycle, step execution, agentic loops, MCP tool calls, handoffs, and streaming.

## Event Types

| Event | Emitted When |
|-------|-------------|
| `WorkflowStarted` | Workflow execution begins |
| `WorkflowCompleted` | Workflow finishes (success or failure) |
| `StepStarted` | A step begins executing |
| `StepCompleted` | A step finishes |
| `StepFailed` | A step throws or returns failure |
| `StateChanged` | Workflow state is modified |
| `EdgeEvaluated` | A condition on an edge is evaluated |
| `CheckpointSaved` | A checkpoint is persisted |
| `InterruptRequested` | A step requests an interrupt |
| `AgentIterationStarted` | An agent loop begins a new iteration |
| `ToolCallStarted` / `ToolCallCompleted` | A tool is invoked |
| `HandoffInitiated` | An agent delegates to another agent |
| `SessionTurnCompleted` | A conversational turn finishes |
| `WorkflowForked` | Execution is forked from a checkpoint |

## Built-in Sinks

**ConsoleEventSink** — Logs events to the console with color coding:

```csharp
services.AddSpectra(builder =>
{
    builder.AddEventSink<ConsoleEventSink>();
});
```

**CompositeEventSink** — Forwards events to multiple sinks:

```csharp
builder.AddEventSink(new CompositeEventSink(
    new ConsoleEventSink(),
    new MyCustomSink()
));
```

**StreamingEventSink** — Pushes events to an `IEventStream` for real-time consumption (SSE, WebSocket):

```csharp
builder.AddEventSink<StreamingEventSink>();
```

**NullEventSink** — Discards all events (for testing or when you don't need observability).

## Writing a Custom Event Sink

```csharp
public class ApplicationInsightsSink : IEventSink
{
    private readonly TelemetryClient _telemetry;

    public ApplicationInsightsSink(TelemetryClient telemetry) => _telemetry = telemetry;

    public Task EmitAsync(WorkflowEvent evt, CancellationToken ct)
    {
        _telemetry.TrackEvent(evt.Type, new Dictionary<string, string>
        {
            ["workflowName"] = evt.WorkflowName,
            ["nodeId"] = evt.NodeId ?? "",
            ["runId"] = evt.RunId
        });
        return Task.CompletedTask;
    }
}
```

## OpenTelemetry Integration

Spectra includes an `ActivitySource` for distributed tracing:

```csharp
using var activity = SpectraActivitySource.Source.StartActivity("workflow.run");
activity?.SetTag(SpectraTags.WorkflowName, workflow.Name);
```

The `WorkflowRunner` and `ParallelScheduler` create spans automatically. To export traces, configure OpenTelemetry as you normally would in your .NET application:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(SpectraActivitySource.SourceName)
        .AddConsoleExporter());
```
