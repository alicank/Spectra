# Subgraphs

A **subgraph** is a nested workflow that runs inside a parent workflow with its own isolated state. Use subgraphs to break complex workflows into reusable, testable modules — each with clear input/output boundaries.

**StepType:** `"subgraph"`

---

## Why Subgraphs?

Large workflows can become hard to reason about. Subgraphs let you:

- **Encapsulate complexity** — A 10-node RAG pipeline becomes a single node in the parent workflow.
- **Reuse logic** — Define a workflow once, embed it in multiple parents.
- **Isolate state** — The child workflow cannot accidentally read or overwrite parent state.
- **Test independently** — Run the child workflow in isolation with mock inputs.

---

## How It Works

When the engine reaches a subgraph node, it executes four phases:

```
1. Input Mapping      Parent state → Child inputs
                      (parentPath → childKey)

2. Create Child State  Fresh WorkflowState with scoped RunId
                      (parentRunId::subgraphId)

3. Execute Child       Run the child workflow to completion
                      via IWorkflowRunner.RunAsync

4. Output Mapping      Child state → Parent step outputs
                      (childPath → parentKey)
```

The parent and child states are completely separate. Data only flows through the explicit mappings you define.

---

## Defining a Subgraph

### Step 1: Define the Child Workflow

```csharp
var ragPipeline = Spectra.Workflow("rag-pipeline")
    .AddStep("embed", new EmbeddingStep(), inputs: new
    {
        text = "{{inputs.query}}"
    })
    .AddStep("search", new VectorSearchStep(), inputs: new
    {
        embedding = "{{nodes.embed.output.vector}}",
        topK = "{{inputs.topK}}"
    })
    .AddPromptStep("synthesize", agent: "openai", inputs: new
    {
        userPrompt = "Answer based on context: {{nodes.search.output.results}}\n\nQuestion: {{inputs.query}}"
    })
    .Edge("embed", "search")
    .Edge("search", "synthesize")
    .Build();
```

### Step 2: Embed It in a Parent Workflow

```csharp
var parent = Spectra.Workflow("customer-support")
    .AddStep("classify", new ClassifyStep(), inputs: new
    {
        text = "{{inputs.message}}"
    })
    .AddSubgraph("lookup", ragPipeline, subgraph => subgraph
        .MapInput("inputs.message", "query")       // parent path → child key
        .MapInput("inputs.topResults", "topK")
        .MapOutput("nodes.synthesize.output.response", "answer"))  // child path → parent key
    .Edge("classify", "lookup")
    .Build();
```

### JSON Definition

```json
{
  "nodes": [
    {
      "id": "lookup",
      "stepType": "subgraph",
      "inputs": { "__subgraphId": "rag-pipeline" }
    }
  ],
  "subgraphs": [
    {
      "id": "rag-pipeline",
      "inputMappings": {
        "inputs.message": "query",
        "inputs.topResults": "topK"
      },
      "outputMappings": {
        "nodes.synthesize.output.response": "answer"
      },
      "workflow": { ... }
    }
  ]
}
```

---

## State Mapping

### Input Mappings

Input mappings copy values from the **parent state** into the **child's inputs**:

```
Parent Path                    →  Child Input Key
─────────────────────────────────────────────────
inputs.message                 →  query
inputs.topResults              →  topK
nodes.classify.output.category →  category
context.userId                 →  userId
```

The parent path is resolved using `StateMapper.GetValueFromPath`, which navigates the parent's `WorkflowState` tree (inputs, nodes, context, artifacts).

### Output Mappings

Output mappings copy values from the **child state** into the parent step's **outputs**:

```
Child Path                          →  Parent Output Key
───────────────────────────────────────────────────────
nodes.synthesize.output.response    →  answer
nodes.search.output.resultCount     →  totalResults
```

These outputs are then available to other parent nodes via `{{nodes.lookup.output.answer}}`.

### Default Behavior (No Output Mappings)

If you don't define any output mappings, the subgraph exposes the child's full `Context` and `Artifacts` dictionaries:

```csharp
// In downstream parent nodes:
// {{nodes.lookup.output.childContext}}
// {{nodes.lookup.output.childArtifacts}}
```

!!! tip "Be Explicit"
    Always define output mappings for production workflows. Exposing the entire child state is useful for debugging but creates tight coupling.

### Forwarding Inputs

Any inputs on the subgraph node that don't start with `__` are automatically forwarded to the child state's inputs — unless an input mapping already defines that key. This is convenient for passing simple values without explicit mappings.

---

## Error Propagation

If the child workflow produces errors, the subgraph step fails:

```
Child completes with errors
  → SubgraphStep returns StepResult.Fail
  → Error message: "Subgraph 'rag-pipeline' completed with errors: ..."
  → All child errors are joined with "; "
```

If the child workflow throws an exception (unhandled crash), the subgraph step also fails with the exception details.

!!! note
    Child workflow errors don't automatically trigger the parent's error handling. The subgraph node is simply marked as failed, and the parent's edge evaluation proceeds normally — you can add conditional edges to handle the failure case.

---

## RunId Scoping

The child workflow gets a scoped `RunId` derived from the parent:

```
Parent RunId:  run-abc-123
Child RunId:   run-abc-123::rag-pipeline
```

This makes it easy to trace parent-child relationships in logs and the event sink. The `CorrelationId` is set to the parent's `RunId`.

---

## Nested Subgraphs

Subgraphs can contain other subgraphs. There's no hard depth limit, but keep in mind:

- Each level adds a `::subgraphId` segment to the `RunId`.
- State is isolated at every level — data must flow through explicit mappings.
- Debugging deeply nested workflows is harder. Consider flattening when nesting exceeds 2-3 levels.

---

## Use Cases

### RAG Pipeline

Encapsulate retrieval-augmented generation (embedding → search → synthesis) as a reusable subgraph that any workflow can embed.

### Multi-Stage Processing

Break a complex pipeline (ingest → validate → transform → enrich → store) into stages, each as a subgraph with clear boundaries.

### A/B Testing

Run two different subgraph implementations and compare outputs:

```csharp
parent
    .AddSubgraph("model-a", pipelineV1, ...)
    .AddSubgraph("model-b", pipelineV2, ...)
    .AddStep("compare", new CompareStep(), inputs: new
    {
        resultA = "{{nodes.model-a.output.answer}}",
        resultB = "{{nodes.model-b.output.answer}}"
    })
    .Edge("model-a", "compare")
    .Edge("model-b", "compare")
```

### Isolation for Testing

Run a subgraph workflow independently with test inputs:

```csharp
var testState = new WorkflowState { WorkflowId = "rag-pipeline" };
testState.Inputs["query"] = "What is Spectra?";
testState.Inputs["topK"] = 5;

var result = await runner.RunAsync(ragPipeline, testState);
```