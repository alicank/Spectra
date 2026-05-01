# Events & Observability

Spectra emits structured events at every stage of workflow execution. This is how you observe, log, debug, and trace what's happening.

## Event System

All events flow through `IEventSink`:

```csharp
public interface IEventSink
{
    Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default);
}
```

Events are emitted for workflow lifecycle, step execution, state changes, branch evaluation, parallel batches, agentic loops, MCP tool calls, handoffs, sessions, resilience, and streaming.

## Event Types

| Event | Emitted When |
|-------|-------------|
| `WorkflowStartedEvent` | Workflow execution begins |
| `WorkflowCompletedEvent` | Workflow finishes |
| `WorkflowResumedEvent` | Workflow resumes from a checkpoint |
| `WorkflowForkedEvent` | Execution is forked from a checkpoint |
| `StepStartedEvent` | A step begins executing |
| `StepCompletedEvent` | A step finishes, including failed step results |
| `StepInterruptedEvent` | A step is interrupted |
| `StateChangedEvent` | Workflow state is modified |
| `BranchEvaluatedEvent` | A condition or default edge is evaluated |
| `ParallelBatchStartedEvent` / `ParallelBatchCompletedEvent` | A parallel batch starts or finishes |
| `TokenStreamEvent` | A token chunk is emitted |
| `AgentIterationEvent` | An agent loop iteration completes |
| `AgentToolCallEvent` | A tool is invoked inside an agent loop |
| `AgentCompletedEvent` | An agent loop ends |
| `AgentHandoffEvent` / `AgentHandoffBlockedEvent` | A handoff is accepted or blocked |
| `AgentDelegationStartedEvent` / `AgentDelegationCompletedEvent` | A supervisor delegates work and receives the result |
| `AgentEscalationEvent` | An agent escalates due to failure or budget exhaustion |
| `SessionTurnCompletedEvent` | A conversational turn finishes |
| `SessionAwaitingInputEvent` | A session waits for the next user message |
| `SessionCompletedEvent` | A session exits |
| `FallbackTriggeredEvent` | Provider fallback moves to the next candidate |
| `QualityGateRejectedEvent` | A response fails a quality gate |
| `FallbackExhaustedEvent` | All fallback candidates fail |
| `ToolCircuitStateChangedEvent` | A tool circuit breaker changes state |
| `ToolCallSkippedEvent` | A tool call is skipped by an open circuit |
| `McpServerConnectedEvent` / `McpServerDisconnectedEvent` | An MCP server connects or disconnects |
| `McpToolCallEvent` / `McpToolCallBlockedEvent` | An MCP tool runs or is blocked |

## Built-in Sinks

**ConsoleEventSink** — Logs events to the console:

```csharp
services.AddSpectra(builder =>
{
    builder.AddConsoleEvents();
});
```

**CompositeEventSink** — Forwards events to multiple sinks. When you register more than one sink, Spectra builds the composite for you:

```csharp
builder.AddConsoleEvents();
builder.AddEventSink(new MyCustomSink());
```

**StreamingEventSink** — Pushes events into the channel used by `runner.StreamAsync(...)`. The runner creates this internally when streaming is used.

**NullEventSink** — Discards all events when no sink is configured.

## Writing a Custom Event Sink

```csharp
public class ApplicationInsightsSink : IEventSink
{
    private readonly TelemetryClient _telemetry;

    public ApplicationInsightsSink(TelemetryClient telemetry) => _telemetry = telemetry;

    public Task PublishAsync(WorkflowEvent evt, CancellationToken ct = default)
    {
        _telemetry.TrackEvent(evt.EventType, new Dictionary<string, string>
        {
            ["workflowId"] = evt.WorkflowId,
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
        .AddSource(SpectraActivitySource.Name)
        .AddConsoleExporter());
```
