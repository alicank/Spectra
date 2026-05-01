---
description: "Manage Spectra prompts as reusable Markdown templates with front-matter, variables, file-based loading, and hot reload."
---

# Prompt Management

Spectra lets you manage prompts as reusable templates instead of hardcoded strings.

Instead of embedding long system prompts directly in workflow code, you can store them in Markdown files, give them stable IDs, attach metadata, and reference them from agents or steps.

This makes prompts easier to:

- reuse across workflows
- version in Git
- review like normal project assets
- update without rewriting workflow logic
- organize by role, task, or domain

In Spectra, prompt management is the layer between **prompt authoring** and **prompt execution**.

- you **author** prompts as templates
- Spectra **loads** them into a registry
- steps and agents **resolve** them by ID
- the renderer **fills in variables** at runtime

---

## Why manage prompts outside code?

For small experiments, inline prompt strings are fine.

But once workflows grow, inline prompts become harder to maintain:

- the same prompt logic gets duplicated
- prompt edits become mixed with code changes
- long prompt text makes workflows harder to read
- it becomes harder to review prompt changes separately

External prompt files solve that.

A typical Spectra setup keeps workflow structure in code or JSON, while prompt content lives in a `prompts/` directory.

That separation makes workflows easier to understand and prompts easier to evolve.

---

## The recommended approach: file-based prompts

The most common setup is to store prompts as Markdown files.

Example:

```text
prompts/
├── agents/
│   ├── coder.md
│   ├── reviewer.md
│   └── planner.md
├── shared/
│   └── safety-guidelines.md
└── tasks/
    └── summarize.md
```

This structure works well because it mirrors how teams think:

- `agents/` for reusable system prompts
- `tasks/` for user-task templates
- `shared/` for common prompt fragments or guidance

You then register the directory during startup:

```csharp
builder.AddPromptsFromDirectory("./prompts");
```

Once registered, prompts can be referenced by ID from agents and workflow steps.

---

## How prompt IDs work

Each file gets a prompt ID based on its path.

The prompt directory root is stripped and the `.md` extension is removed.

| File Path | Prompt ID |
| --- | --- |
| `prompts/agents/coder.md` | `agents/coder` |
| `prompts/shared/safety-guidelines.md` | `shared/safety-guidelines` |
| `prompts/tasks/summarize.md` | `tasks/summarize` |

That means a file like:

```text
prompts/agents/coder.md
```

can be referenced as:

```csharp
.WithSystemPromptRef("agents/coder")
```

This gives you stable, readable prompt references throughout your workflows.

---

## Writing a prompt template

Prompt templates are normal text with `{{variable}}` placeholders.

Example:

```text
You are a {{role}} working on the {{project}} project.
Summarize the following text in {{language}}:

{{inputs.text}}
```

At runtime, Spectra replaces the placeholders with values from workflow state and step inputs.

This makes prompts reusable without hardcoding environment-specific or task-specific values.

---

## Front-matter metadata

Prompt files can include front-matter at the top.

Example:

```markdown
---
id: agents/coder
name: Coder Agent
description: System prompt for the coding specialist
version: "2.1"
variables:
  - language
  - framework
---

You are a senior {{language}} developer specializing in {{framework}}.
Write clean, well-tested code. Always include error handling.
Follow the project's existing code style.
```

Front-matter is useful for:

- giving prompts human-readable names
- documenting what a prompt is for
- tracking prompt versions
- listing expected variables
- attaching extra metadata for tooling or governance

### Supported fields

| Field | Type | Description |
| --- | --- | --- |
| `id` | string | Overrides the file-path-derived ID |
| `name` | string | Human-readable name |
| `description` | string | What the prompt is for |
| `version` | string | Version tag for tracking changes |
| `variables` | list | Expected `{{variable}}` names |

Any other front-matter keys are stored in the prompt metadata dictionary.

### Important note about `variables`

The `variables` list is for documentation and discoverability.

It does **not** enforce runtime validation by itself.

In other words:

- it tells people what the prompt expects
- it does not block rendering if a value is missing

Actual missing-variable behavior is controlled by the renderer mode.

---

## Using prompt files with agents

One of the best uses of prompt management is storing agent system prompts as files.

Example:

```csharp
builder.AddPromptsFromDirectory("./prompts");

builder.AddAgent("coder", "openai", "gpt-4o", agent => agent
    .WithSystemPromptRef("agents/coder"));
```

Here, the agent does not carry a large inline system prompt.

Instead, it references:

```text
prompts/agents/coder.md
```

This keeps agent registration clean and makes the prompt easier to update independently.

---

## Using prompt files with workflow steps

Workflow steps can also use inline prompts or prompt references.

Example:

```csharp
workflow.AddAgentNode("code", "coder", node => node
    .WithUserPrompt("Implement a REST endpoint for: {{inputs.task}}"));
```

In this example:

- the **system prompt** comes from the file-backed prompt registry
- the **user prompt** is inline
- both can still use template variables

This is a good pattern when:

- the reusable identity lives in a file
- the task-specific instruction is generated inline by the workflow

---

## How variables are resolved

When Spectra renders a prompt, it runs two passes.

**Pass 1 — namespaced state paths** (`StateMapper`)

Expressions like `{{inputs.task}}`, `{{context.result}}`, and `{{nodes.fetch.data}}` are resolved directly against `WorkflowState`. These use the dotted-namespace syntax and are handled before any flat variable lookup.

**Pass 2 — bare variable names** (`PromptRenderer`)

After the first pass, any remaining `{{key}}` placeholders are resolved from a flat merged dictionary. Sources are merged in this order, with later values winning:

1. `state.Inputs`
2. `state.Context`
3. step `Inputs` (excluding internal `__` keys)

That means node-level inputs can override broader workflow values when needed.

A template can freely mix both styles:

```text
You are working on {{inputs.project}}.   ← namespaced path (pass 1)
Summarize the following in {{language}}: ← bare key (pass 2)

{{inputs.text}}
```

---

## Missing variable behavior

Sometimes a prompt references a variable that is not available at render time.

The renderer supports three behaviors:

| Mode | Behavior |
| --- | --- |
| `LeaveTemplate` | Keeps `{{variable}}` unchanged in the output |
| `ReplaceWithEmpty` | Replaces missing variables with an empty string |
| `ThrowException` | Throws `KeyNotFoundException` during rendering |

The default is:

```text
LeaveTemplate
```

That default is helpful during development because unresolved placeholders remain visible instead of silently disappearing.

---

## Hot reload during development

When you are iterating on prompts, restarting the app after every prompt edit is slow.

Spectra supports hot reload for file-based prompt registries. Pass `watch: true` when constructing `FilePromptRegistry` directly — `AddPromptsFromDirectory` does not enable watching:

```csharp
builder.AddPrompts(new FilePromptRegistry("./prompts", watch: true));
```

With file watching enabled, changes to `.md` prompt files are picked up automatically.

This is especially useful when you are:

- tuning agent behavior
- refining system prompts
- testing workflow wording
- collaborating with non-developers on prompt text

---

## In-memory prompts

For tests, experiments, or generated prompts, Spectra also supports in-memory registration.

```csharp
var registry = new InMemoryPromptRegistry();

registry.Register(new PromptTemplate
{
    Id = "test/greeting",
    Content = "Hello, {{name}}! Welcome to {{project}}."
});

builder.AddPrompts(registry);
```

This is useful when you want:

- lightweight tests
- prompts created programmatically
- no file-system dependency
- a simple setup for prototypes

If you do not register prompts from a directory, an in-memory registry is the simplest fallback.

---

## How Spectra resolves prompts

When a step needs a prompt, Spectra checks a resolution chain.

The most important idea is:

- inline prompts take highest priority
- referenced prompts come next
- agent-level defaults are used when step-level values are absent

### System prompt resolution

For system prompts, Spectra checks in this order:

```text
1. systemPrompt input
2. agent.SystemPromptRef
3. agent.SystemPrompt
4. promptId input
5. no system prompt
```

### User prompt resolution

For user prompts, Spectra checks in this order:

```text
1. userPrompt input
2. userPromptRef input
3. no user prompt
```

This gives you flexibility:

- keep simple prompts inline
- move reusable prompts into files
- define stable agent identities once
- override behavior per step when needed

---

## A practical mental model

A good way to think about Spectra prompt management is:

- **prompt file** = reusable content asset
- **prompt ID** = stable reference to that asset
- **registry** = where prompts are loaded and looked up
- **renderer** = fills template variables at runtime
- **agent or step** = the consumer of the rendered prompt

Once that clicks, the system becomes easy to reason about.

---

## Under the hood: `PromptTemplate`

Internally, each prompt is represented as a `PromptTemplate`.

```csharp
public class PromptTemplate
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public List<string> Variables { get; init; } = [];
    public Dictionary<string, object?> Metadata { get; init; } = [];
    public string? FilePath { get; init; }
    public DateTimeOffset? LoadedAt { get; init; }
}
```

At a high level:

- `Id` is how the prompt is referenced
- `Content` is the template text
- `Variables` documents expected inputs
- the rest is metadata about the prompt and where it came from

Most users do not need to construct `PromptTemplate` directly unless they are working with in-memory registries or custom prompt tooling.

---

## Recommended project pattern

A strong default setup for real projects looks like this:

- keep reusable system prompts in `prompts/agents/`
- keep task prompts in `prompts/tasks/`
- register prompts once at startup
- reference prompts by ID from agents
- use inline user prompts only when the instruction is highly workflow-specific

This keeps the system easy to scale as workflows grow.

---

## Common issues

### Prompt ID not found

Make sure the registered prompt directory and the referenced prompt ID match.

For example:

```text
prompts/agents/coder.md
```

becomes:

```text
agents/coder
```

### Prompt changes are not picked up

Hot reload only works when file watching is enabled.

### Variables are not being replaced

Check that the values exist in workflow inputs, context, or step inputs, and verify the renderer's missing-variable mode.

### Prompt metadata is present but not enforced

Fields like `variables` help document a prompt, but they do not perform runtime validation by themselves.

---

## What's next?

<div class="grid cards" markdown>

- **Providers**

  Connect agents to OpenAI, Anthropic, Gemini, Ollama, OpenRouter, or compatible APIs.

  [:octicons-arrow-right-24: Providers](providers.md)

- **Prompt Steps**

  Learn where prompts are consumed in workflow execution.

  [:octicons-arrow-right-24: Prompt & Structured Output](prompt-steps.md)

- **Agent Step**

  See how system prompts shape autonomous agent behavior.

  [:octicons-arrow-right-24: Agent Step](agent-step.md)

</div>