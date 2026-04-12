# Getting Started

Build and run your first Spectra workflow in a new .NET app.

In this guide, you will:

1. create a console project
2. install Spectra
3. register one LLM provider
4. run a simple workflow
5. connect two nodes with shared state

By the end, you will understand Spectra's core model:

- **nodes do work**
- **edges define flow**
- **state carries data between steps**

---

## Prerequisites

You need:

- .NET 10 SDK
- an API key for a supported provider such as OpenRouter, OpenAI, or Anthropic

This guide uses **OpenRouter** for the simplest first setup.

---

## Create a project

```bash
dotnet new console -n HelloSpectra
cd HelloSpectra
dotnet add package Spectra
dotnet add package Spectra.Extensions
```

- `Spectra` contains the workflow engine and builders
- `Spectra.Extensions` adds provider integrations like OpenAI, Anthropic, Gemini, Ollama, and OpenRouter

---

## Set your API key

=== "bash"

    ```bash
    export OPENROUTER_API_KEY="your-api-key"
    ```

=== "PowerShell"

    ```powershell
    $env:OPENROUTER_API_KEY="your-api-key"
    ```

!!! tip "Using another provider?"
    You can swap `AddOpenRouter(...)` for `AddOpenAI(...)`, `AddAnthropic(...)`, `AddGemini(...)`, or `AddOllama(...)`.

    Your workflow definition stays the same. Only provider setup changes.

---

## Your first workflow

Create or replace `Program.cs` with this:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Registration;
using Spectra.Workflow;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;
                config.DefaultModel = "openai/gpt-4o-mini";
            });

            spectra.AddConsoleEvents();
        });
    })
    .Build();

var workflow = WorkflowBuilder.Create("hello")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("You are a friendly assistant."))
    .AddAgentNode("greet", "assistant", node => node
        .WithUserPrompt("Say hello to {{inputs.name}} in a creative way.")
        .WithMaxIterations(1))
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState
{
    ["inputs.name"] = "World"
};

var result = await runner.RunAsync(workflow, state);

Console.WriteLine(result["nodes.greet.output"]);
```

---

## What is happening here?

This example has four parts:

### 1. Register Spectra

```csharp
services.AddSpectra(...)
```

This adds the Spectra runtime to the normal .NET dependency injection container.

### 2. Register a provider

```csharp
spectra.AddOpenRouter(...)
```

This tells Spectra how to call a model provider.

### 3. Define a workflow

```csharp
var workflow = WorkflowBuilder.Create("hello") ...
```

This creates a workflow with one node:

- an agent named `assistant`
- a node named `greet`
- a prompt that uses `{{inputs.name}}`

### 4. Run with state

```csharp
var state = new WorkflowState
{
    ["inputs.name"] = "World"
};
```

`WorkflowState` is the shared data for the run.

In Spectra, a common convention is:

- `inputs.*` for incoming values
- `nodes.<nodeId>.output` for node results

That is why the final output is read from:

```csharp
result["nodes.greet.output"]
```

---

## Run it

```bash
dotnet run
```

You should see workflow events in the console, then the generated greeting.

A typical run looks like this:

```text
[WorkflowStarted] hello
[StepStarted] greet
[StepCompleted] greet
[WorkflowCompleted] hello

Hello, World! It's great to meet you.
```

The exact greeting will vary by model.

What matters is:

- the `greet` node runs
- its output is written to workflow state
- you read it back from `nodes.greet.output`

---

## Add a second node

Now let's connect two steps together.

This workflow:

1. generates a greeting
2. passes that greeting to another node
3. translates it to French

```csharp
var workflow = WorkflowBuilder.Create("greet-and-translate")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("You are a helpful assistant."))
    .AddAgentNode("greet", "assistant", node => node
        .WithUserPrompt("Say hello to {{inputs.name}}. Reply with just the greeting.")
        .WithMaxIterations(1))
    .AddAgentNode("translate", "assistant", node => node
        .WithUserPrompt("Translate to French: {{nodes.greet.output}}")
        .WithMaxIterations(1))
    .AddEdge("greet", "translate")
    .Build();
```

The key expression is:

```text
{{nodes.greet.output}}
```

That value comes from the first node's output.

This is Spectra's core model:

- **nodes** perform work
- **edges** connect the steps
- **state** carries values through the graph

Once that clicks, the rest of Spectra becomes much easier to understand.

---

## C# or JSON?

You can define workflows in either format:

- use **C#** when you want workflow definitions close to application code
- use **JSON** when you want workflows to be easy to edit, review, or deploy without recompiling

Start with C# first.

Then move to JSON when you want external workflow definitions.

[See JSON workflow definitions →](concepts/workflows.md)

---

## Common first-run issues

### `OPENROUTER_API_KEY` is null

Your environment variable is not set in the shell where you ran `dotnet run`.

### Provider not found

Make sure you registered the same provider name you use in the workflow:

```csharp
.AddAgent("assistant", "openrouter", ...)
```

### Output key not found

Make sure you read the node output using the correct node id:

```csharp
result["nodes.greet.output"]
```

---

## What's next?

- [Workflows](concepts/workflows.md) — understand nodes, edges, and graph structure
- [State](concepts/state.md) — learn how data moves through a workflow
- [Providers](llm/providers.md) — switch between OpenAI, Anthropic, Gemini, Ollama, and OpenRouter
- [Agent Step](llm/agent-step.md) — customize prompts, iterations, and model behavior
- [Runner](execution/runner.md) — learn how workflows execute