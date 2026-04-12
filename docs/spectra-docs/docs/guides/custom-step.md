# Build a Custom Step

This guide walks you through creating a custom step from scratch — from a minimal implementation to advanced features like streaming, interrupts, and memory access.

---

## When to Build a Custom Step

Spectra's [built-in steps](../concepts/steps.md#built-in-steps) cover LLM completions, agent loops, sessions, subgraphs, and memory. Build a custom step when you need to:

- Call an external API or database
- Transform, validate, or enrich data
- Run domain-specific business logic
- Integrate with a system that isn't an LLM
- Implement a custom orchestration pattern

!!! tip "Rule of Thumb"
    If your logic doesn't need an LLM, you almost certainly need a custom step. If it *does* need an LLM but with non-standard behavior, a custom step wrapping `ILlmClient` directly gives you full control.

---

## Minimal Implementation

Here's the simplest possible step — it counts words in a text input:

```csharp
using Spectra.Contracts.Steps;

public class WordCountStep : IStep
{
    public string StepType => "word_count";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var text = context.Inputs.TryGetValue("text", out var v)
            ? v?.ToString() ?? ""
            : "";

        var count = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
        {
            ["count"] = count,
            ["text"] = text
        }));
    }
}
```

Key points:

- `StepType` is a string identifier. Pick something unique and descriptive.
- Read inputs from `context.Inputs` — these are already resolved (template expressions replaced with real values).
- Return `StepResult.Success(outputs)` with a dictionary of output values.
- The method is `Task`-returning. For synchronous work, use `Task.FromResult`.

---

## Registration

Register your step with the DI container so Spectra knows about it:

```csharp
services.AddSpectra(builder =>
{
    builder.AddStep<WordCountStep>();
});
```

This makes the step available by its `StepType` for both programmatic and JSON-defined workflows.

---

## Using Your Step in a Workflow

### Builder API

```csharp
var workflow = Spectra.Workflow("analyze-text")
    .AddStep("count", new WordCountStep(), inputs: new
    {
        text = "{{inputs.content}}"
    })
    .AddPromptStep("summarize", agent: "openai",
        prompt: "The text has {{nodes.count.output.count}} words. Summarize: {{inputs.content}}")
    .Edge("count", "summarize")
    .Build();
```

### JSON Workflow

```json
{
  "nodes": [
    {
      "id": "count",
      "stepType": "word_count",
      "inputs": {
        "text": "{{inputs.content}}"
      }
    }
  ]
}
```

Template expressions like `{{inputs.content}}` and `{{nodes.count.output.count}}` are resolved by the `StateMapper` before your step executes.

---

## Reading Inputs Safely

The `Inputs` dictionary contains `object?` values. Here are patterns for extracting typed data safely:

```csharp
public Task<StepResult> ExecuteAsync(StepContext context)
{
    // String — with fallback
    var name = context.Inputs.TryGetValue("name", out var n)
        ? n?.ToString() ?? "default"
        : "default";

    // Integer — with parsing
    var limit = 10;
    if (context.Inputs.TryGetValue("limit", out var l) && l is not null
        && int.TryParse(l.ToString(), out var parsed))
    {
        limit = parsed;
    }

    // Boolean
    var verbose = context.Inputs.TryGetValue("verbose", out var vb) && vb is true;

    // List of strings
    var tags = context.Inputs.TryGetValue("tags", out var t) && t is IEnumerable<object> items
        ? items.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList()
        : new List<string>();

    // ... your logic ...
}
```

!!! warning "Don't Assume Types"
    Inputs arrive as `object?`. When workflows are loaded from JSON, a number might be a `JsonElement`, not an `int`. Always parse defensively.

---

## Error Handling

### Expected Errors — Return `StepResult.Fail`

For errors that are part of normal operation (API returned 404, validation failed, data missing):

```csharp
if (string.IsNullOrEmpty(apiKey))
    return StepResult.Fail("API key is required. Set the 'apiKey' input.");

try
{
    var result = await _client.FetchAsync(url, context.CancellationToken);
    return StepResult.Success(new() { ["data"] = result });
}
catch (HttpRequestException ex)
{
    return StepResult.Fail($"API call failed: {ex.Message}", ex);
}
```

### Unexpected Errors — Let Them Propagate

The workflow runner wraps step execution in a try/catch. Unhandled exceptions are caught, logged, and turned into failures automatically. You don't need to catch everything.

### Cancellation — Always Honor It

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    foreach (var batch in batches)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        await ProcessBatchAsync(batch, context.CancellationToken);
    }
    return StepResult.Success();
}
```

---

## Advanced: Streaming

If your step produces text incrementally, you can stream tokens to the caller:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    var result = new StringBuilder();

    if (context.IsStreaming)
    {
        await foreach (var chunk in GenerateChunksAsync(context.CancellationToken))
        {
            result.Append(chunk);
            await context.OnToken!(chunk, context.CancellationToken);
        }
    }
    else
    {
        result.Append(await GenerateFullAsync(context.CancellationToken));
    }

    return StepResult.Success(new() { ["text"] = result.ToString() });
}
```

When `IsStreaming` is `false`, `OnToken` is `null` — always check before calling it.

---

## Advanced: Interrupts (Human-in-the-Loop)

Pause execution and wait for external input:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    var amount = (decimal)context.Inputs["amount"]!;

    if (amount > 10_000)
    {
        var response = await context.InterruptAsync("large-expense", b => b
            .WithTitle("Large Expense Approval")
            .WithPayload(new { amount, vendor = context.Inputs["vendor"] }));

        if (response.Payload.TryGetValue("approved", out var approved) && approved is false)
            return StepResult.Fail("Expense rejected by approver.");
    }

    // Continue processing...
    return StepResult.Success(new() { ["processed"] = true });
}
```

The engine checkpoints the workflow, suspends, and resumes your step when the response arrives. Your code reads as straight-line logic.

---

## Advanced: Memory Access

Read and write to long-term memory that persists across workflow runs:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    if (context.Memory is null)
        return StepResult.Fail("Memory store is not configured.");

    // Recall
    var entry = await context.Memory.GetAsync("preferences", "theme",
        context.CancellationToken);

    // Store
    await context.Memory.SetAsync("preferences", "theme", new MemoryEntry
    {
        Key = "theme",
        Namespace = "preferences",
        Content = "dark",
        UpdatedAt = DateTimeOffset.UtcNow
    }, context.CancellationToken);

    return StepResult.Success(new() { ["theme"] = entry?.Content ?? "light" });
}
```

---

## Advanced: Tracing

Add custom spans and tags to the OpenTelemetry trace:

```csharp
public async Task<StepResult> ExecuteAsync(StepContext context)
{
    context.TracingActivity?.SetTag("step.custom.batch_size", batches.Count);

    using var childSpan = SpectraActivitySource.Source.StartActivity("process-batches");
    // ... work ...

    return StepResult.Success();
}
```

---

## Testing Your Step

Custom steps are easy to unit test — just create a `StepContext` with the inputs you want:

```csharp
[Fact]
public async Task WordCountStep_Counts_Words()
{
    var step = new WordCountStep();

    var context = new StepContext
    {
        RunId = "test-run",
        WorkflowId = "test-workflow",
        NodeId = "count",
        State = new WorkflowState { WorkflowId = "test" },
        CancellationToken = CancellationToken.None,
        Inputs = new Dictionary<string, object?>
        {
            ["text"] = "hello world from spectra"
        }
    };

    var result = await step.ExecuteAsync(context);

    Assert.Equal(StepStatus.Succeeded, result.Status);
    Assert.Equal(4, result.Outputs["count"]);
}
```

!!! tip "Test Failure Cases Too"
    Test missing inputs, null values, and cancellation. Your step should handle these gracefully with `StepResult.Fail`, not with unhandled exceptions.

---

## Quick Reference

| Concept | How |
|---------|-----|
| Identify your step | Set `StepType` to a unique string |
| Read inputs | `context.Inputs.TryGetValue(...)` |
| Return success | `StepResult.Success(outputs)` |
| Return failure | `StepResult.Fail(message, exception?)` |
| Register | `builder.AddStep<YourStep>()` |
| Use in workflow | `.AddStep("nodeId", instance, inputs: ...)` |
| Stream tokens | Check `context.IsStreaming`, call `context.OnToken!()` |
| Pause for human | `await context.InterruptAsync(reason, config)` |
| Access memory | `context.Memory?.GetAsync(...)` |
| Add tracing | `context.TracingActivity?.SetTag(...)` |
| Cancel cleanly | `context.CancellationToken.ThrowIfCancellationRequested()` |