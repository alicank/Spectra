# Interrupts

An **interrupt** pauses workflow execution at any point and waits for external input before continuing. This is Spectra's primitive for human-in-the-loop patterns, approval gates, and external callbacks.

## How Interrupts Work

Any step can request an interrupt by throwing an `InterruptException` or returning `StepStatus.Interrupted`:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
{
    var plan = GenerateDeploymentPlan(context.Inputs);

    // Pause and ask for approval
    throw new InterruptException(
        InterruptRequest.Builder()
            .WithReason("Deployment plan requires approval")
            .WithData("plan", plan)
            .WithOptions("approve", "reject", "modify")
            .Build()
    );
}
```

When an interrupt is thrown:

1. The workflow runner saves a checkpoint
2. The `InterruptRequest` is surfaced to the caller (API response, CLI prompt, UI)
3. Execution stops

When the caller provides a response:

```csharp
var response = new InterruptResponse
{
    Status = InterruptStatus.Approved,
    Data = new Dictionary<string, object> { ["notes"] = "Looks good, ship it" }
};

var result = await runner.ResumeAsync(runId, response);
```

The step re-executes with the interrupt response available in its context.

## IInterruptHandler

For programmatic interrupt handling (e.g., auto-approval in CI, or routing to a queue):

```csharp
public interface IInterruptHandler
{
    Task<InterruptResponse> HandleAsync(InterruptRequest request, CancellationToken ct);
}
```

Register a handler:

```csharp
builder.AddInterruptHandler<SlackApprovalHandler>();
```

## InterruptRequest Builder

```csharp
var request = InterruptRequest.Builder()
    .WithReason("Review the generated code before applying")
    .WithData("files", modifiedFiles)
    .WithData("diff", diffOutput)
    .WithOptions("apply", "reject", "edit")
    .WithTimeout(TimeSpan.FromHours(24))
    .Build();
```

## Difference from HumanGateStep

The interrupt primitive is more flexible than a dedicated gate step:

- **Interrupts** can be thrown from *any* step, including inside `AgentStep` iterations
- **HumanGateStep** is a standalone node in the graph — it's a convenience wrapper around the interrupt primitive
- Use interrupts when you need conditional pausing inside complex step logic
- Use `HumanGateStep` when you just need a simple approve/reject gate between two nodes
