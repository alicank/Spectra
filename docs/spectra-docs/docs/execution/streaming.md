---
description: "Stream Spectra workflow events and token output in real time."
---

# Streaming

Spectra can stream workflow events in real time while a workflow is running.

Instead of waiting for the full workflow to finish, you can consume events as they happen.

This is useful for:

- live UIs
- chat interfaces
- progress indicators
- server-sent event endpoints
- real-time token output

---

## Stream a workflow

Use `IWorkflowRunner.StreamAsync(...)` to execute a workflow and consume events with `await foreach`.

```csharp
await foreach (var evt in runner.StreamAsync(workflow, StreamMode.Tokens, initialState))
{
    switch (evt)
    {
        case TokenStreamEvent token:
            Console.Write(token.Token);
            break;

        case StepCompletedEvent step:
            Console.WriteLine($"\n[{step.NodeId}] completed in {step.Duration}");
            break;

        case WorkflowCompletedEvent done:
            Console.WriteLine($"\nWorkflow finished. Success: {done.Success}");
            break;
    }
}
```

There is also an overload that accepts `RunContext`:

```csharp
await foreach (var evt in runner.StreamAsync(workflow, StreamMode.Tokens, state, runContext))
{
    // ...
}
```

---

## Stream modes

`StreamMode` controls how much of the event stream you receive.

| Mode | Includes | Best for |
| --- | --- | --- |
| `Tokens` | All events, including token deltas | Live LLM output |
| `Messages` | All events except token deltas | Step-level progress without token noise |
| `Updates` | Step completion, state changes, workflow completion, interrupts | Progress views and dashboards |
| `Values` | State changes and workflow completion only | Reactive state consumers |
| `Custom` | Full stream, filter it yourself | Advanced consumers |

Use the narrowest mode that matches your UI or integration.

---

## Token streaming

When you run a workflow with `StreamAsync(...)`, Spectra enables token-level streaming for LLM steps that support it.

Each token chunk is emitted as a `TokenStreamEvent`.

| Field | Description |
| --- | --- |
| `Token` | The emitted text chunk |
| `TokenIndex` | Position of the chunk within the current output |

### Which steps support token streaming

Token streaming is supported by:

- `PromptStep`
- `AgentStep`
- `SessionStep`

For `AgentStep` and `SessionStep`, only the **final response** is streamed.

Tool-calling iterations are not streamed token-by-token because they must be fully parsed first.

---

## Streaming does not replace normal events

Streaming adds a live delivery path, but it does not replace your normal event sinks.

That means:

- your existing event sink can still write logs, audit records, or database events
- `StreamAsync(...)` gives you a second live stream for the current consumer

This is important if you want both:

- persistent observability
- live UI updates

---

## How it works

Internally, the runner writes workflow events into a streamable channel while still publishing them to the configured event sink.

You usually do not need to manage this directly.

The important behavior is:

- steps emit events as they run
- token-capable steps emit token events
- the runner yields those events through `IAsyncEnumerable<WorkflowEvent>`

---

## `IEventStream`

For advanced event consumption, Spectra also provides `IEventStream`.

```csharp
public interface IEventStream
{
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken ct = default)
        where TEvent : WorkflowEvent;

    IAsyncEnumerable<WorkflowEvent> SubscribeAllAsync(
        StreamMode mode = StreamMode.Updates, CancellationToken ct = default);
}
```

Use this when you want:

- typed subscriptions
- event-specific consumers
- filtered access without manually switching over the whole event stream

Example:

- `SubscribeAsync<StepCompletedEvent>()` for step completions only
- `SubscribeAllAsync(StreamMode.Values)` for state and completion events only

---

## A simple mental model

- `RunAsync(...)` waits for the workflow result
- `StreamAsync(...)` gives you the workflow result as a live event stream

That is the core distinction.

---

## What's next?

<div class="grid cards" markdown>

- **Events**

  See the full event model and built-in sinks.

  [:octicons-arrow-right-24: Events](../observability/events.md)

- **Workflow Runner**

  Learn how the runner drives streaming and execution.

  [:octicons-arrow-right-24: Runner](runner.md)

</div>