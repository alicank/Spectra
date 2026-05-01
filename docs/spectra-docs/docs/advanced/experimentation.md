# A/B Testing & Experimentation

Spectra's [checkpoint forking](../execution/time-travel.md) lets you branch execution from any saved checkpoint in a completed or in-progress run. This turns every checkpoint into a laboratory - change one variable, re-run from that point, compare outcomes.

---

## The Pattern

```
Original Run "run-001"
  0: classify   -> "urgent"                 checkpoint 0
  1: extract    -> pulled 42 records         checkpoint 1  < fork point
  2: summarize  -> used gpt-4o, cost $0.12   checkpoint 2
  3: publish    -> sent report               checkpoint 3

Fork A "fork-gpt4o-mini" (from checkpoint 1, model = gpt-4o-mini)
  2: summarize  -> used gpt-4o-mini, cost $0.01
  3: publish    -> sent report

Fork B "fork-claude" (from checkpoint 1, model = claude-sonnet)
  2: summarize  -> used claude-sonnet, cost $0.08
  3: publish    -> sent report
```

All three runs share the same classify and extract work. Only the summarize step (and everything after it) is re-executed with different parameters.

---

## Example: Compare Models

Which model produces the best summary for your use case? Fork from the same checkpoint and find out:

```csharp
// Original run already completed with gpt-4o
var originalRunId = "run-001";
var forkPoint = 1; // Checkpoint after "extract", before "summarize"

// Fork A: Try gpt-4o-mini (cheaper)
var forkA = await runner.ForkAndRunAsync(workflow, originalRunId, forkPoint,
    newRunId: "experiment-mini",
    stateOverrides: new WorkflowState
    {
        Context = { ["__agentOverrides"] = new Dictionary<string, AgentDefinition>
        {
            ["summarizer"] = new AgentDefinition
            {
                Id = "summarizer", Provider = "openai", Model = "gpt-4o-mini",
                Temperature = 0.3, MaxTokens = 4096
            }
        }}
    });

// Fork B: Try Claude
var forkB = await runner.ForkAndRunAsync(workflow, originalRunId, forkPoint,
    newRunId: "experiment-claude",
    stateOverrides: new WorkflowState
    {
        Context = { ["__agentOverrides"] = new Dictionary<string, AgentDefinition>
        {
            ["summarizer"] = new AgentDefinition
            {
                Id = "summarizer", Provider = "anthropic", Model = "claude-sonnet-4-20250514",
                Temperature = 0.3, MaxTokens = 4096
            }
        }}
    });

// Compare latest checkpoints
var originalCheckpoint = await checkpointStore.LoadAsync("run-001");
var miniCheckpoint = await checkpointStore.LoadAsync("experiment-mini");
var claudeCheckpoint = await checkpointStore.LoadAsync("experiment-claude");
```

---

## Example: Tune Parameters

Same model, different temperature - which produces more consistent output?

```csharp
var temperatures = new[] { 0.0, 0.3, 0.7, 1.0 };
var results = new Dictionary<double, WorkflowState>();

foreach (var temp in temperatures)
{
    var forkId = $"experiment-temp-{temp}";
    var result = await runner.ForkAndRunAsync(workflow, originalRunId, forkPoint,
        newRunId: forkId,
        stateOverrides: new WorkflowState
        {
            Inputs = { ["temperature"] = temp }
        });
    results[temp] = result;
}

// Analyze: which temperature produced the best summary?
```

---

## Example: Test Prompt Variations

Fork from the same data and try different prompts:

```csharp
var prompts = new Dictionary<string, string>
{
    ["concise"] = "Summarize in 3 bullet points.",
    ["detailed"] = "Write a comprehensive summary with key findings and recommendations.",
    ["executive"] = "Write a one-paragraph executive summary for C-level stakeholders."
};

foreach (var (style, prompt) in prompts)
{
    await runner.ForkAndRunAsync(workflow, originalRunId, forkPoint,
        newRunId: $"experiment-{style}",
        stateOverrides: new WorkflowState
        {
            Inputs = { ["summaryPrompt"] = prompt }
        });
}
```

---

## Tracing Experiment Lineage

Every forked run starts with a checkpoint carrying `ParentRunId` and `ParentCheckpointIndex`. Use `GetLineageAsync` to trace the ancestry:

```csharp
var lineage = await checkpointStore.GetLineageAsync("experiment-claude");
// Returns the ancestor checkpoint chain, ending with experiment-claude's first checkpoint.
```

This creates a natural audit trail: "experiment-claude was forked from run-001 at checkpoint 1, with the summarizer agent overridden to use Claude."

---

## Best Practices

**Fork from deterministic points.** Fork after data-loading steps (which are expensive and deterministic) and before LLM steps (which are cheap to re-run and variable).

**Use meaningful run IDs.** `experiment-claude-temp03-v2` is much easier to analyze than `fork-abc-123`.

**Compare with events.** Each forked run emits its own [events](../observability/events.md). Query events by `RunId` to compare token usage, latency, and step outcomes across experiments.

**Clean up.** Experiments generate checkpoints. Use `PurgeAsync` or retention policies to clean up after analysis:

```csharp
await checkpointStore.PurgeAsync("experiment-mini");
```

---

## What's Next

<div class="grid cards" markdown>

-   **Time Travel & Forking**

    The underlying fork mechanism.

    [:octicons-arrow-right-24: Time Travel](../execution/time-travel.md)

-   **Checkpointing**

    How experiment state is stored.

    [:octicons-arrow-right-24: Checkpointing](../execution/checkpointing.md)

-   **Providers**

    Configure the models you want to compare.

    [:octicons-arrow-right-24: Providers](../llm/providers.md)

</div>