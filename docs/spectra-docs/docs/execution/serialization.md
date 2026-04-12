# Serialization

Spectra workflows and agents can be defined in JSON files. This enables version control, CI/CD deployment, and collaboration between developers who write code and workflow designers who prefer configuration.

---

## Workflow JSON

A `.workflow.json` file maps directly to the same structure you'd build with the `WorkflowBuilder` API:

```json
{
  "id": "hello-world",
  "name": "Hello World Workflow",
  "version": 1,
  "entryNodeId": "greet",
  "nodes": [
    {
      "id": "greet",
      "stepType": "echo",
      "inputs": { "message": "Hello from Spectra!" }
    },
    {
      "id": "farewell",
      "stepType": "echo",
      "inputs": { "message": "Goodbye! You said: {{nodes.greet.output.message}}" }
    }
  ],
  "edges": [
    { "from": "greet", "to": "farewell" }
  ]
}
```

Load workflows from a directory:

```csharp
builder.AddWorkflowsFromDirectory("./workflows");
```

Or use the store directly:

```csharp
var store = new JsonFileWorkflowStore("./workflows");
var workflow = store.Get("hello-world");
```

File names don't matter — the `id` field inside the JSON is used for lookup.

For the full workflow definition format — nodes, edges, conditions, agents, subgraphs, state fields — see [Workflows & Graphs](../concepts/workflows.md).

---

## Agent JSON

Agent definitions can also live in `.agent.json` files:

```json
{
  "id": "researcher",
  "provider": "openai",
  "model": "gpt-4o",
  "temperature": 0.3,
  "maxTokens": 4096,
  "systemPrompt": "You are a research assistant.",
  "handoffTargets": ["coder"],
  "conversationScope": "Full"
}
```

```csharp
builder.AddAgentsFromDirectory("./agents");
```

See [Providers](../llm/providers.md) for the full agent definition reference.

---

## Checkpoint Serialization

Checkpoints use `CheckpointSerializer` with versioned JSON. Schema version is stamped on every checkpoint — old checkpoints load after upgrading Spectra, but downgrading rejects newer formats safely. This is an internal concern unless you're building a custom `ICheckpointStore`. See [Checkpointing](checkpointing.md).

---

## What's Next

<div class="grid cards" markdown>

-   **Workflows & Graphs**

    Full reference for the workflow definition format.

    [:octicons-arrow-right-24: Workflows](../concepts/workflows.md)

-   **Providers**

    Agent definition reference.

    [:octicons-arrow-right-24: Providers](../llm/providers.md)

</div>