# SubgraphComposition

**Child workflows with isolated state and explicit data contracts.**

This sample demonstrates **subgraphs** — Spectra's mechanism for composing workflows from smaller, reusable workflow units. Each subgraph runs as an isolated child workflow with its own state, connected to the parent through explicit input/output mappings.

## What you'll learn

- How to define inline subgraphs in a workflow JSON
- How input mappings flow data from parent → child
- How output mappings flow results from child → parent
- How child state isolation prevents accidental data leakage
- How to register `SubgraphStep` while avoiding the circular DI dependency

## The pipeline

```
┌─────────┐    ┌──────────────────────┐    ┌──────────────────────┐    ┌───────────────┐
│ analyze  │───▶│  seo-optimization    │───▶│  social-media-kit    │───▶│ compile-brief │
│ (prompt) │    │     (subgraph)       │    │     (subgraph)       │    │   (prompt)    │
└─────────┘    │                      │    │                      │    └───────────────┘
               │  ┌─────────────────┐ │    │  ┌────────────────┐  │
               │  │extract-keywords │ │    │  │  write-tweet   │  │
               │  └───────┬─────────┘ │    │  └───────┬────────┘  │
               │          ▼           │    │          ▼           │
               │  ┌─────────────────┐ │    │  ┌────────────────┐  │
               │  │   write-meta    │ │    │  │ write-linkedin │  │
               │  └───────┬─────────┘ │    │  └───────┬────────┘  │
               │          ▼           │    │          ▼           │
               │  ┌─────────────────┐ │    │  ┌────────────────┐  │
               │  │ optimize-title  │ │    │  │write-instagram │  │
               │  └─────────────────┘ │    │  └────────────────┘  │
               └──────────────────────┘    └──────────────────────┘
```

**4 main nodes** orchestrate the flow. **2 subgraphs** contain **3 nodes each**, for a total of **10 LLM calls** across **8 agents**.

## How subgraphs work

### Definition

A subgraph is declared in the `subgraphs` array of the workflow JSON. Each subgraph has:

- **`id`** — a unique identifier referenced by parent nodes via `subgraphId`
- **`workflow`** — a complete inline `WorkflowDefinition` (nodes, edges, entry point)
- **`inputMappings`** — maps parent state paths → child input keys
- **`outputMappings`** — maps child state paths → parent state paths

```json
{
  "id": "seo-subgraph",
  "workflow": {
    "id": "seo-optimization-workflow",
    "nodes": [ ... ],
    "edges": [ ... ],
    "entryNodeId": "extract-keywords"
  },
  "inputMappings": {
    "Inputs.draft": "draft",
    "Context.analyze": "analysis"
  },
  "outputMappings": {}
}
```

### Parent node

The parent workflow references the subgraph with a node of `stepType: "subgraph"`:

```json
{
  "id": "seo-optimization",
  "stepType": "subgraph",
  "subgraphId": "seo-subgraph"
}
```

### State isolation

When the subgraph executes, `SubgraphStep` creates a **fresh `WorkflowState`** for the child. The parent's context, artifacts, and errors are never visible to the child. Only the values specified in `inputMappings` are copied into `childState.Inputs`.

After execution, only the values specified in `outputMappings` flow back to the parent. If no output mappings are defined (as in this sample), the full `childContext` and `childArtifacts` dictionaries are exposed — useful for development and debugging.

### Why this matters

- **Reusability** — the SEO subgraph could be used in multiple parent workflows
- **Encapsulation** — child workflows can't accidentally corrupt parent state
- **Testability** — each subgraph can be tested independently
- **Clarity** — input/output contracts make data flow explicit

## The circular dependency problem

`SubgraphStep` needs `IWorkflowRunner` to execute child workflows. But `IWorkflowRunner` needs `IStepRegistry`, and `IStepRegistry` contains `SubgraphStep` — circular.

This sample solves it by registering `SubgraphStep` **after** the host is built:

```csharp
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra => { ... });
        // Don't register SubgraphStep here — it would deadlock DI
    })
    .Build();

// After build: both IWorkflowRunner and IStepRegistry are resolved, no cycle
var stepRegistry = host.Services.GetRequiredService<IStepRegistry>();
var runner = host.Services.GetRequiredService<IWorkflowRunner>();
stepRegistry.Register(new SubgraphStep(runner));
```

## Running

```bash
# Set your API key
export OPENROUTER_API_KEY=sk-or-...

# Run with the built-in sample draft
dotnet run --project samples/SubgraphComposition

# Or provide your own article text
dotnet run --project samples/SubgraphComposition -- "Your article draft here..."
```

## Expected output

```
═══ Content Publishing Pipeline ═══
Draft: Agentic AI frameworks are reshaping how enterprises build automation...

Loaded: Content Publishing Pipeline
  Main nodes: 4
  Subgraphs:  2
    └─ seo-subgraph: 3 nodes (SEO Optimization)
    └─ social-subgraph: 3 nodes (Social Media Kit)
  Agents:     8

Running pipeline...
──────────────────────────────────────────────────────────

[StepStarted]  analyze (prompt)
[StepCompleted] analyze → Succeeded
[StepStarted]  seo-optimization (subgraph)
  [StepStarted]  extract-keywords (prompt)     ← child workflow
  [StepCompleted] extract-keywords → Succeeded
  [StepStarted]  write-meta (prompt)
  [StepCompleted] write-meta → Succeeded
  [StepStarted]  optimize-title (prompt)
  [StepCompleted] optimize-title → Succeeded
[StepCompleted] seo-optimization → Succeeded
[StepStarted]  social-media-kit (subgraph)
  [StepStarted]  write-tweet (prompt)          ← child workflow
  ...
[StepCompleted] social-media-kit → Succeeded
[StepStarted]  compile-brief (prompt)
[StepCompleted] compile-brief → Succeeded

──────────────────────────────────────────────────────────

✓ Pipeline completed successfully

══ Final Publishing Brief ══
1. Content Analysis
   Topic: Agentic AI | Audience: Engineering leaders | Tone: Professional
   ...
2. SEO Package
   Keywords: agentic AI, workflow orchestration, ...
   Meta: Build adaptive AI pipelines with composable subgraphs...
   ...
3. Social Media Kit
   Twitter: 🤖 Agentic AI is changing how enterprises build...
   LinkedIn: The shift from rigid DAGs to adaptive pipelines...
   Instagram: 🚀 The future of enterprise automation is here...
4. Publishing Checklist
   ...
```

## Key concepts demonstrated

| Concept | Where |
|---|---|
| Inline subgraph definition | `subgraphs[]` in workflow JSON |
| Input mapping (parent → child) | `Inputs.draft → draft`, `Context.analyze → analysis` |
| Output mapping (child → parent) | Default: full `childContext` + `childArtifacts` |
| State isolation | Child never sees parent's context |
| Subgraph node | `stepType: "subgraph"` + `subgraphId` |
| Circular DI workaround | Post-build `stepRegistry.Register(new SubgraphStep(runner))` |
| JSON-defined workflow | Everything declarative, no C# workflow builder needed |