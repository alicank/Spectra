# Interrupts

An **interrupt** pauses workflow execution at any point and waits for external input before continuing. This is Spectra's primitive for human-in-the-loop patterns, approval gates, and external callbacks.

## How Interrupts Work

Any step can request an interrupt by calling `context.InterruptAsync(...)`:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
{
    var plan = GenerateDeploymentPlan(context.Inputs);

    var response = await context.InterruptAsync("Deployment plan requires approval", b => b
        .WithTitle("Approve Deployment Plan")
        .WithPayload(new { plan }));

    if (response.Rejected)
        return StepResult.Fail("Deployment rejected: " + response.Comment);

    return StepResult.Success(new() { ["approved"] = true });
}
```

When an interrupt is raised:

1. The workflow runner saves a checkpoint
2. The `InterruptRequest` is surfaced to the caller (API response, CLI prompt, UI)
3. Execution stops

When the caller provides a response:

```csharp
var result = await runner.ResumeWithResponseAsync(
    workflow,
    runId: "run-abc",
    interruptResponse: InterruptResponse.ApprovedResponse(
        respondedBy: "alice",
        comment: "Looks good, ship it"));
```

The step continues from the `context.InterruptAsync(...)` call with the response returned to it.

## IInterruptHandler

For programmatic interrupt handling (e.g., auto-approval in CI, or routing to a queue):

```csharp
public interface IInterruptHandler
{
    Task<InterruptResponse> HandleAsync(InterruptRequest request, CancellationToken cancellationToken = default);
}
```

Register a handler:

```csharp
builder.AddInterruptHandler(new SlackApprovalHandler());
```

## InterruptRequest Builder

The builder is accessed via the `configure` action in `context.InterruptAsync(reason, configure)`:

```csharp
var response = await context.InterruptAsync("Review the generated code before applying", b => b
    .WithTitle("Code Review Required")
    .WithDescription("Please review the diff before it is applied")
    .WithPayload(new { files = modifiedFiles, diff = diffOutput })
    .WithMetadata("source", "code-gen-step"));
```

Available builder methods: `.WithTitle(string)`, `.WithDescription(string)`, `.WithPayload(object?)`, `.WithMetadata(string, object?)`.
