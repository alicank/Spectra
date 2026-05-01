# Subgraphs

A **subgraph** is a nested workflow that runs inside a parent workflow with its own isolated state. Use subgraphs to break complex workflows into reusable, testable modules - each with clear input/output boundaries.

**StepType:** `"subgraph"`

---

## Why Subgraphs?

Large workflows can become hard to reason about. Subgraphs let you:

- **Encapsulate complexity** - A 10-node RAG pipeline becomes a single node in the parent workflow.
- **Reuse logic** - Define a workflow once, embed it in multiple parents.
- **Isolate state** - The child workflow cannot accidentally read or overwrite parent state.
- **Test independently** - Run the child workflow in isolation with mock inputs.

---

## How It Works

When the engine reaches a subgraph node, it executes these phases:

```
1. Input Mapping       Parent state -> child inputs
                       (parentPath -> childKey)

2. Create Child State  Fresh WorkflowState with scoped RunId
                       (parentRunId::subgraphId)

3. Agent Inheritance   Parent agents are copied into the child workflow
                       when the child does not already define that agent ID

4. Execute Child       Run the child workflow to completion
                       via IWorkflowRunner.RunAsync

5. Output Mapping      Child state -> parent step outputs
                       (childPath -> parentStatePath)
```

The parent and child states are separate. Data flows into the child through input mappings or explicit non-internal node inputs, and child results flow back through output mappings.

---

## Defining a Subgraph

### Step 1: Define the Child Workflow

```csharp
var ragPipeline = WorkflowBuilder.Create("rag-pipeline")
    .AddAgent("answerer", "openai", "gpt-4o")
    .AddNode("embed", "embedding", node => node
        .WithParameter("text", "{{inputs.query}}"))
    .AddNode("search", "vector-search", node => node
        .WithParameter("embedding", "{{context.embed.vector}}")
        .WithParameter("topK", "{{inputs.topK}}"))
    .AddNode("synthesize", "prompt", node => node
        .WithAgent("answerer")
        .WithParameter("userPrompt",
            "Answer based on context: {{context.search.results}}\n\nQuestion: {{inputs.query}}"))
    .AddEdge("embed", "search")
    .AddEdge("search", "synthesize")
    .Build();
```

`embedding` and `vector-search` are example custom step types; they must be registered in the step registry for the workflow to run.

### Step 2: Embed It in a Parent Workflow

```csharp
var parent = WorkflowBuilder.Create("customer-support")
    .AddNode("classify", "classify", node => node
        .WithParameter("text", "{{inputs.message}}"))
    .AddSubgraph("rag-pipeline", ragPipeline, subgraph => subgraph
        .MapInput("inputs.message", "query")
        .MapInput("inputs.topResults", "topK")
        .MapOutput("context.synthesize.response", "context.answer"))
    .AddSubgraphNode("lookup", "rag-pipeline")
    .AddEdge("classify", "lookup")
    .Build();
```

### JSON Definition

```json
{
  "nodes": [
    {
      "id": "lookup",
      "stepType": "subgraph",
      "subgraphId": "rag-pipeline"
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
        "context.synthesize.response": "context.answer"
      },
      "workflow": { }
    }
  ]
}
```

---

## State Mapping

### Input Mappings

Input mappings copy values from the **parent state** into the **child's inputs**:

```
Parent Path                    ->  Child Input Key
--------------------------------------------------
inputs.message                 ->  query
inputs.topResults              ->  topK
nodes.classify.category        ->  category
context.userId                 ->  userId
```

The parent path is resolved using `StateMapper.GetValueFromPath`, which navigates the parent's `WorkflowState` tree (`inputs`, `nodes`, `context`, `artifacts`). Path roots are case-insensitive; dictionary keys after the root must match the stored key.

Any inputs on the subgraph node that don't start with `__` are also forwarded to the child state's inputs, unless an input mapping already defines that key.

### Output Mappings

Output mappings copy values from the **child state** into the parent step's outputs. The workflow runner then applies those outputs to the parent state path:

```
Child Path                       ->  Parent State Path
-----------------------------------------------------
context.synthesize.response      ->  context.answer
artifacts.search.resultCount     ->  context.totalResults
```

These mapped values are then available to other parent nodes via their state paths, for example `{{context.answer}}`.

### Default Behavior (No Output Mappings)

If you don't define any output mappings, the subgraph exposes the child's full `Context` and `Artifacts` dictionaries as step outputs:

```csharp
// In downstream parent nodes:
// {{nodes.lookup.childContext}}
// {{nodes.lookup.childArtifacts}}
```

The runner also maps those default outputs to `context.childContext` and `context.childArtifacts` for the subgraph node.

!!! tip "Be Explicit"
    Always define output mappings for production workflows. Exposing the entire child state is useful for debugging but creates tight coupling.

---

## Error Propagation

If the child workflow produces errors, the subgraph step fails:

```
Child completes with errors
  -> SubgraphStep returns StepResult.Fail
  -> Error message: "Subgraph 'rag-pipeline' completed with errors: ..."
  -> All child errors are joined with "; "
```

If the child workflow throws an exception (unhandled crash), the subgraph step also fails with the exception details.

!!! note
    Child workflow errors are reported as a failed subgraph step. The parent run records the failure; add conditional edges or failure handling around the subgraph node if you want a recovery path.

---

## RunId Scoping

The child workflow gets a scoped `RunId` derived from the parent:

```
Parent RunId:  run-abc-123
Child RunId:   run-abc-123::rag-pipeline
```

This makes it easy to trace parent-child relationships in logs and the event sink. The child state's `CorrelationId` is set to the parent's `RunId`.

---

## Nested Subgraphs

Subgraphs can contain other subgraphs. There's no hard depth limit, but keep in mind:

- Each level adds a `::subgraphId` segment to the `RunId`.
- State is isolated at every level - data must flow through explicit mappings or forwarded node inputs.
- Parent workflow agents are inherited when the child does not define an agent with the same ID.
- Debugging deeply nested workflows is harder. Consider flattening when nesting exceeds 2-3 levels.

---

## Use Cases

### RAG Pipeline

Encapsulate retrieval-augmented generation (embedding -> search -> synthesis) as a reusable subgraph that any workflow can embed.

### Multi-Stage Processing

Break a complex pipeline (ingest -> validate -> transform -> enrich -> store) into stages, each as a subgraph with clear boundaries.

### A/B Testing

Run two different subgraph implementations and compare outputs:

```csharp
var parent = WorkflowBuilder.Create("ab-test")
    .AddSubgraph("model-a", pipelineV1, sg => sg
        .MapOutput("context.answer", "context.resultA"))
    .AddSubgraph("model-b", pipelineV2, sg => sg
        .MapOutput("context.answer", "context.resultB"))
    .AddSubgraphNode("run-a", "model-a")
    .AddSubgraphNode("run-b", "model-b")
    .AddNode("compare", "compare", node => node
        .WaitForAll()
        .WithParameter("resultA", "{{context.resultA}}")
        .WithParameter("resultB", "{{context.resultB}}"))
    .AddEdge("run-a", "compare")
    .AddEdge("run-b", "compare")
    .Build();
```

### Isolation for Testing

Run a subgraph workflow independently with test inputs:

```csharp
var testState = new WorkflowState { WorkflowId = "rag-pipeline" };
testState.Inputs["query"] = "What is Spectra?";
testState.Inputs["topK"] = 5;

var result = await runner.RunAsync(ragPipeline, testState);
```