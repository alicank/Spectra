---
description: "Understand workflow state in Spectra. Learn how inputs, node outputs, template expressions, reducers, and validation work."
---

# State Management

State is how data moves through a Spectra workflow.

A workflow begins with some input values. As nodes run, they read from state and write new values back into it. Later nodes can then use those values as inputs for their own work.

That shared-state model is what makes a graph useful: each step can build on what earlier steps produced.

---

## The basic idea

In Spectra, state follows a path-based convention:

```text
inputs.name
inputs.document
nodes.fetch.output
nodes.analyze.summary
global.correlationId
```

- `inputs.*` — values you provide at the start of the workflow
- `nodes.<nodeId>.*` — values produced by nodes during execution
- `global.*` — shared workflow-wide values

For example, a workflow might start with:

```csharp
var state = new WorkflowState
{
    ["inputs.name"]  = "World",
    ["inputs.topic"] = "workflow orchestration"
};
```

After execution, it may also contain values like `nodes.greet.output`, `nodes.summarize.output`, and `nodes.classify.score`.

---

## How nodes use state

Nodes read from state through template expressions and write results back when they complete.

For example, one node might fetch a document, and another might summarize it using the first node's output:

```json
{
  "id": "summarize",
  "stepType": "Prompt",
  "inputs": {
    "prompt": "Summarize this text: {{nodes.fetch.output.content}}",
    "maxTokens": 500
  }
}
```

Here, `fetch` runs first, its output is written into workflow state, and `summarize` reads `nodes.fetch.output.content` at runtime. This lets you chain steps without tightly coupling them.

---

## Template expressions

Spectra resolves `{{...}}` expressions against the current workflow state at runtime. These expressions can appear in prompts, node inputs, configuration values, and other mapped inputs.

| Expression                        | Resolves to                                          |
| --------------------------------- | ---------------------------------------------------- |
| `{{inputs.name}}`                 | An input value provided at the start of the workflow |
| `{{nodes.stepId.output}}`         | The full output of a node                            |
| `{{nodes.stepId.output.field}}`   | A nested field inside a node output                  |
| `{{global.key}}`                  | A shared global value                                |

> **Key convention:** `{{nodes.someNode.output}}` is how downstream nodes refer to upstream results.

---

## WorkflowState

Every workflow run has a `WorkflowState` object that accumulates data over time — workflow inputs, node outputs, and shared global values. You interact with it through path-based keys and template expressions rather than manipulating the dictionaries directly.

```csharp
public class WorkflowState
{
    public Dictionary<string, object> Inputs { get; }
    public Dictionary<string, object> Nodes  { get; }
    public Dictionary<string, object> Global { get; }
}
```

The important idea is not the internal storage shape — it is that workflow data remains available throughout the entire run.

---

## Parallel branches and reducers

When multiple branches run in parallel and both write to the same state key, Spectra needs to know how to combine those values. The default behavior is **last write wins**, which is fine for some workflows but not all.

**State reducers** let you define a merge strategy:

```csharp
public interface IStateReducer
{
    object Reduce(object current, object incoming);
}
```

```csharp
services.AddSpectra(builder =>
{
    builder.AddStateReducer("results", new AppendListReducer());
    builder.AddStateReducer("score",   new SumReducer());
});
```

Typical reducer use cases:

- Append results from parallel branches into a single list
- Sum numeric values from multiple nodes
- Merge maps or structured objects
- Apply custom deterministic merge rules

Reducers matter most in fan-out and parallel workflows. See [Parallel Execution](parallel-execution.md) for the broader execution model.

---

## State validation and schemas

You can optionally define a schema for workflow state to require certain inputs before execution starts, validate expected data shapes, or catch missing values early.

```csharp
public interface IStateSchema
{
    IReadOnlyList<StateFieldDefinition> Fields { get; }
    ValidationResult Validate(WorkflowState state);
}
```

Instead of failing deep inside a node, the workflow fails early with a clear validation error — especially useful for workflows that depend on required inputs such as `inputs.customerId`, `inputs.document`, or `inputs.priority`.

---

## How template resolution works

When Spectra resolves input mappings, it walks the input structure and replaces `{{...}}` expressions using the current workflow state:

1. Finds template expressions inside mapped values
2. Resolves each expression against the current state
3. Traverses nested paths
4. Preserves the resolved value type where possible

This means expressions can appear inside nested objects, not only in flat string fields:

```json
{
  "request": {
    "query":   "{{inputs.query}}",
    "context": "{{nodes.search.output}}"
  }
}
```

---

## Practical guidance

Use these conventions consistently:

- Put incoming workflow values under `inputs.*`
- Read previous node results from `nodes.<nodeId>.*`
- Use `global.*` only for truly shared workflow-wide values
- Add reducers when parallel branches write to the same key
- Add schema validation when workflows need stronger guarantees

A good state model makes large workflows much easier to understand.

---

## Lifecycle example

A simple workflow state lifecycle:

**Initial state**

```text
inputs.name = "Ava"
```

**After `greet` runs**

```text
nodes.greet.output = "Hello, Ava!"
```

**After `translate` runs**

```text
nodes.translate.output = "Bonjour, Ava !"
```

The second node reads the first node's output via `{{nodes.greet.output}}` — that pattern is the backbone of data flow in Spectra.

---

## Where to go next

- [Workflows](workflows.md) — how state fits into nodes and edges
- [Steps](steps.md) — the units of work that read and write state
- [Conditional Edges](conditional-edges.md) — branch based on workflow data
- [Parallel Execution](parallel-execution.md) — how concurrent branches interact with state
- [Runner](../execution/runner.md) — how workflows execute at runtime