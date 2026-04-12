---
description: "Pause and resume Spectra workflows with declarative or programmatic interrupts."
---

# Interrupts

An **interrupt** pauses workflow execution and waits for an external response before continuing.

Use interrupts for:

- approval gates
- human review
- external callbacks
- pause-and-resume workflow patterns

Spectra supports two interrupt styles:

- **declarative** — configure the pause on the node
- **programmatic** — trigger the pause from step code

---

## Choose the interrupt style

| Style | Best for |
| --- | --- |
| **Declarative** | Simple approval points before or after a node |
| **Programmatic** | Steps that need to decide at runtime whether to pause |

---

## Declarative interrupts

Declarative interrupts let you pause without writing step code.

You configure the node with `InterruptBefore` or `InterruptAfter`.

```csharp
var workflow = WorkflowBuilder.Create("review-pipeline")
    .AddNode("generate", "prompt", node => node
        .WithParameter("userPrompt", "Write a report on {{inputs.topic}}"))

    .AddNode("publish", "prompt", node => node
        .WithInterruptBefore("Review the generated report before publishing")
        .WithParameter("userPrompt", "Format for publication: {{nodes.generate.output.response}}"))

    .AddEdge("generate", "publish")
    .Build();
```

### When they pause

| Interrupt | When it pauses | Outputs applied? |
| --- | --- | --- |
| `InterruptBefore` | Before the step runs | No |
| `InterruptAfter` | After the step runs, before edge evaluation | Yes |

This is the simplest way to add approval gates into a workflow.

---

## Programmatic interrupts

Programmatic interrupts are triggered from inside step code.

Use them when the step needs to decide at runtime whether a human or external system should review something.

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    var plan = GenerateDeploymentPlan(context);

    var response = await context.InterruptAsync("deployment-approval", b => b
        .WithTitle("Approve Deployment Plan")
        .WithPayload(new { plan, estimatedCost = "$42.50" }));

    if (response.Rejected)
        return StepResult.Fail("Deployment rejected: " + response.Comment);

    return StepResult.Success(new() { ["approved"] = true });
}
```

When execution resumes, the `InterruptAsync(...)` call returns the response and the step continues from there.

That makes programmatic interrupts feel like a normal async pause point in your code.

---

## What happens when an interrupt is raised

When an interrupt occurs, the runner:

1. captures the interrupt request
2. checkpoints the workflow state
3. marks the run as interrupted
4. stops execution until a response is provided

That is what makes interrupts safe across restarts and long delays.

---

## Resume after an interrupt

To continue a paused workflow, provide an `InterruptResponse`:

```csharp
var result = await runner.ResumeWithResponseAsync(
    workflow,
    runId: "run-abc",
    interruptResponse: InterruptResponse.ApprovedResponse(
        respondedBy: "alice",
        comment: "Ship it"));
```

The runner loads the interrupted checkpoint and resumes execution.

### How resume behaves

- for **declarative** interrupts, execution simply continues past the pause point
- for **programmatic** interrupts, the response is returned back to `context.InterruptAsync(...)`

That is the key difference.

---

## Interrupt requests and responses

Interrupts use two payload types:

- `InterruptRequest` — what the workflow is asking for
- `InterruptResponse` — what the human or external system sends back

### `InterruptRequest`

```csharp
public sealed record InterruptRequest
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required string NodeId { get; init; }
    public string? Reason { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public object? Payload { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
}
```

### `InterruptResponse`

```csharp
public sealed record InterruptResponse
{
    public required InterruptStatus Status { get; init; }

    public bool Approved => Status == InterruptStatus.Approved;
    public bool Rejected => Status == InterruptStatus.Rejected;
    public bool TimedOut => Status == InterruptStatus.TimedOut;
    public bool Cancelled => Status == InterruptStatus.Cancelled;

    public string? RespondedBy { get; init; }
    public string? Comment { get; init; }
    public object? Payload { get; init; }
    public DateTimeOffset RespondedAt { get; init; }
}
```

### Factory helpers

```csharp
InterruptResponse.ApprovedResponse(payload: data, respondedBy: "alice", comment: "Looks good");
InterruptResponse.RejectedResponse(respondedBy: "bob", comment: "Needs more testing");
InterruptResponse.TimedOutResponse(comment: "No response within 24 hours");
InterruptResponse.CancelledResponse(comment: "Workflow cancelled by admin");
```

---

## Optional automation with `IInterruptHandler`

Interrupts do not always need a human.

You can register an `IInterruptHandler` to handle them automatically.

```csharp
public interface IInterruptHandler
{
    Task<InterruptResponse> HandleAsync(
        InterruptRequest request, CancellationToken ct = default);
}
```

Register one like this:

```csharp
builder.AddInterruptHandler(new AutoApproveHandler());
```

If a handler is configured and returns a response, execution continues without pausing.

This is useful for:

- CI auto-approval
- routing to queues
- webhook-backed approval systems
- environment-specific automation

### Example

```csharp
public class CiAutoApproveHandler : IInterruptHandler
{
    public Task<InterruptResponse> HandleAsync(InterruptRequest request, CancellationToken ct)
    {
        return Task.FromResult(InterruptResponse.ApprovedResponse(
            respondedBy: "ci-pipeline",
            comment: "Auto-approved in CI environment"));
    }
}
```

---

## Interrupts in multi-agent workflows

Interrupts show up naturally in multi-agent patterns.

### Approval-gated handoffs

If an agent uses:

```csharp
.WithHandoffPolicy(HandoffPolicy.RequiresApproval)
```

then a handoff request pauses for approval before the transfer happens.

### Human escalation

If an agent uses:

```csharp
.WithEscalationTarget("human")
```

then Spectra interrupts the workflow instead of failing or stopping silently.

See [Guard Rails](../multi-agent/guard-rails.md) for the full multi-agent safety model.

---

## A simple mental model

- **declarative interrupt** = "pause here"
- **programmatic interrupt** = "pause here if the step decides it should"
- **resume** = "continue with this response"

That is the core workflow.

---

## What's next?

<div class="grid cards" markdown>

- **Checkpointing**

  See how interrupted runs are persisted and resumed.

  [:octicons-arrow-right-24: Checkpointing](checkpointing.md)

- **Time Travel**

  Resume or fork from an interrupted checkpoint.

  [:octicons-arrow-right-24: Time Travel](time-travel.md)

- **Workflow Runner**

  Learn how the execution loop handles interrupts.

  [:octicons-arrow-right-24: Runner](runner.md)

</div>