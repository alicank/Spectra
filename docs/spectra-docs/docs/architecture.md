This page should feel a bit more “map of the system” and a bit less like a dense essay.

For architecture, mermaid **does** help here, but only for the big structural views:

* package/layer dependency view
* runtime execution flow
* maybe registry/extensibility map, but that may be overkill

## Brainstorming: how to make this page better

### 1. Start with the purpose of the architecture page

Readers coming here usually want one of three things:

* “Which package do I need?”
* “Where does this feature live?”
* “Where do I extend Spectra safely?”

So the page should answer those quickly.

### 2. Use mermaid for the package/layer view

This is the most helpful place for a diagram. It makes the package boundaries and dependency direction much easier to understand.

### 3. Use a second mermaid for runtime flow

A simple flow from `RunAsync` → runner/scheduler → steps/providers/tools/checkpoints/events is useful. Keep it high-level, not too many boxes.

### 4. Keep registries, but frame them as “runtime extension points”

That is the main thing extenders care about. The registry table is good, but it should read as:

* this is what Spectra plugs together at runtime
* these are the places you register or replace things

### 5. Keep “key design decisions”, but make them shorter

Good content, but too long in paragraph form. For architecture pages, crisp explanations work better.

### 6. Fix internal links/paths

Some of the current links look inconsistent with the rest of your docs, like `../others/...` and `llm/resilience.md`. This page should link to the actual guide locations already present in your nav.

---

## Recommended markdown rewrite

````markdown
---
description: "Understand Spectra's architecture, package layers, runtime components, and extension points."
---

# Architecture

Spectra is built in layers.

Each layer has a clear job:

- **contracts** define the shapes
- **kernel** runs workflows
- **extensions** connect external providers
- **registration** wires everything into .NET DI
- **ASP.NET Core** optionally exposes workflows over HTTP

This page is for people who want to:

- understand where features live
- extend Spectra safely
- contribute without guessing which layer to touch

---

## Package layers

Spectra is split into focused NuGet packages with one-way dependencies.

```mermaid
flowchart TD
    A[Spectra.AspNetCore<br/>HTTP endpoints] --> B[Spectra<br/>DI registration and builders]
    B --> C[Spectra.Kernel<br/>runtime engine]
    B --> D[Spectra.Extensions<br/>provider integrations]
    C --> E[Spectra.Contracts<br/>interfaces and models]
    D --> E
````

### Package responsibilities

| Package              | What it contains                                          | Use it when...                                                    |
| -------------------- | --------------------------------------------------------- | ----------------------------------------------------------------- |
| `Spectra.Contracts`  | Interfaces, enums, data models                            | You are building on top of Spectra and want contracts only        |
| `Spectra.Kernel`     | Runner, scheduler, built-in steps, registries, decorators | You want the runtime engine                                       |
| `Spectra.Extensions` | Built-in LLM providers and integrations                   | You want OpenAI, Anthropic, Ollama, Gemini, or OpenRouter support |
| `Spectra`            | `AddSpectra(...)`, fluent builders, hosted service        | This is the normal app entry point                                |
| `Spectra.AspNetCore` | `MapSpectra(...)` HTTP integration                        | You want to expose workflows over HTTP                            |

### Why `Spectra.Contracts` matters

`Spectra.Contracts` has no runtime implementations.

That is intentional.

It means you can implement things like:

* `ILlmProvider`
* `ICheckpointStore`
* `IMemoryStore`
* `ITool`
* `IStep`

without pulling in the full runtime.

That keeps extensions portable and testable.

---

## Runtime architecture

At runtime, the workflow engine coordinates steps, providers, tools, state, events, and persistence.

```mermaid
flowchart LR
    A[RunAsync] --> B[WorkflowRunner]
    B --> C{Parallel work?}
    C -->|No| D[Sequential execution]
    C -->|Yes| E[ParallelScheduler]

    D --> F[Resolve inputs and context]
    E --> F

    F --> G[Execute IStep]
    G --> H[Write outputs to state]
    H --> I[Evaluate edges]
    I --> J[Emit events]
    I --> K[Save checkpoint]
```

At a high level:

* the **runner** controls execution
* the **scheduler** handles concurrency when the graph allows it
* **steps** do the work
* **state** carries data forward
* **events** expose what happened
* **checkpoints** make execution resumable

See [Workflow Runner](execution/runner.md) for the detailed execution loop.

---

## Runtime components

When `AddSpectra(...)` runs, Spectra wires a set of core registries and services into DI.

These are the main runtime extension points.

| Registry / service  | What it holds                              | Default implementation            | Common way to extend            |
| ------------------- | ------------------------------------------ | --------------------------------- | ------------------------------- |
| `IProviderRegistry` | Registered `ILlmProvider` instances        | `InMemoryProviderRegistry`        | Add or replace providers        |
| `IStepRegistry`     | Registered `IStep` instances by `StepType` | `InMemoryStepRegistry`            | `builder.AddStep(...)`          |
| `IAgentRegistry`    | Global agent definitions                   | `InMemoryAgentRegistry`           | `builder.AddAgent(...)`         |
| `IToolRegistry`     | Registered `ITool` instances               | `InMemoryToolRegistry`            | `builder.AddTool(...)`          |
| `ICheckpointStore`  | Workflow checkpoint persistence            | `InMemoryCheckpointStore`         | `builder.AddCheckpoints(...)`   |
| `IMemoryStore`      | Long-term memory backend                   | `InMemoryMemoryStore`             | `builder.AddMemory(...)`        |
| `IEventSink`        | Event delivery                             | `NullEventSink` or composite sink | `builder.AddEventSink(...)`     |
| `IThreadManager`    | Thread lifecycle and querying              | `InMemoryThreadManager`           | `builder.AddThreadManager(...)` |

For local development, the in-memory defaults are enough.

For production, the most common replacements are:

* database-backed checkpoint store
* durable memory store
* custom event sink
* custom thread manager

---

## Where to extend Spectra

Most extension work in Spectra means implementing one interface and registering it.

| What you want to extend       | Interface             | Typical use                                   |
| ----------------------------- | --------------------- | --------------------------------------------- |
| Custom LLM backend            | `ILlmProvider`        | Connect a provider Spectra does not ship with |
| Custom workflow node behavior | `IStep`               | Add your own domain logic                     |
| Custom agent function         | `ITool`               | Expose an API, database, or system action     |
| Custom checkpoint backend     | `ICheckpointStore`    | Durable workflow persistence                  |
| Custom memory backend         | `IMemoryStore`        | Durable or searchable long-term memory        |
| Custom thread storage         | `IThreadManager`      | Durable thread lifecycle and retention        |
| Custom observability sink     | `IEventSink`          | Send events to your own monitoring stack      |
| Custom fallback validation    | `IQualityGate`        | Reject low-quality fallback responses         |
| Custom parallel state merge   | `IStateReducer`       | Control how shared keys merge                 |
| Custom edge condition logic   | `IConditionEvaluator` | Replace the default condition evaluator       |

### Guides for the most common extension points

* [Build a Custom Step](guides/custom-step.md)
* [Build a Custom LLM Provider](guides/custom-provider.md)
* [Build Your Own Checkpoint Store](guides/build-your-own-checkpoint-store.md)
* [Build Your Own Memory Store](guides/build-your-own-memory-store.md)
* [Build Your Own Thread Manager](guides/build-your-own-thread-manager.md)

---

## Execution model

Spectra is a graph runtime, not just a linear pipeline engine.

That means a workflow can:

* run sequentially
* branch conditionally
* fan out in parallel
* loop
* pause for input
* hand off between agents
* resume from checkpoints

This is why the runtime is centered around:

* `WorkflowRunner`
* `ParallelScheduler`
* `StateMapper`
* `StepResult`
* checkpoint and event pipelines

The workflow definition describes the graph.

The runner turns that graph into a live execution.

---

## Key design decisions

### Graph-first workflows

Spectra models workflows as directed graphs instead of fixed pipelines.

That makes it possible to combine:

* branches
* loops
* sessions
* handoffs
* parallel execution

in one model.

### JSON and code define the same shape

A workflow can be built in C# or loaded from JSON.

Both produce the same underlying `WorkflowDefinition`.

That makes workflows easy to:

* version
* store
* generate
* load dynamically

### Interfaces over inheritance

Spectra uses interfaces for extension points instead of deep base-class hierarchies.

That keeps custom implementations:

* smaller
* easier to test
* less coupled to internal code

### Decorators for resilience

LLM resilience features are composed as decorators rather than one giant component.

Typical composition looks like:

* cache
* fallback
* retry/timeout
* provider client

Each layer has one responsibility.

### Cancellation flows through everything

The execution path passes `CancellationToken` through async operations.

That matters for:

* long-running agents
* worker shutdown
* streaming
* session resumption
* graceful interruption

---

## How to think about the layers

A simple mental model:

* **Contracts** say what the pieces look like
* **Kernel** says how workflows run
* **Extensions** say how Spectra talks to outside systems
* **Spectra** says how everything gets wired into an app
* **AspNetCore** says how workflows are exposed over HTTP

If you are contributing or extending Spectra, that usually tells you exactly where your change belongs.

---

## What most contributors never need to touch

In normal usage, you usually do **not** need to modify:

* the runner
* the scheduler
* built-in registries
* core contracts

Most customization happens by adding implementations around the architecture, not by rewriting the architecture itself.

That is a deliberate design goal.

---

## What's next?

<div class="grid cards" markdown>

* **Workflow Runner**

  See how the execution loop works in detail.

  [:octicons-arrow-right-24: Runner](execution/runner.md)

* **Providers**

  Learn how built-in providers fit into the architecture.

  [:octicons-arrow-right-24: Providers](llm/providers.md)

* **Build a Custom Step**

  Add your own workflow node type.

  [:octicons-arrow-right-24: Custom Step](guides/custom-step.md)

* **Build a Custom LLM Provider**

  Connect Spectra to a custom AI backend.

  [:octicons-arrow-right-24: Custom Provider](guides/custom-provider.md)

</div>
```
