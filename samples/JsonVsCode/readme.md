# JsonVsCode

Define the same workflow in two ways: in C# with `WorkflowBuilder`, or in JSON with a workflow file.

This sample shows that both approaches produce the same workflow shape and can be executed the same way.

## What it demonstrates

* code-first workflows with `WorkflowBuilder`
* JSON-first workflows with `JsonFileWorkflowStore`
* the same graph represented in C# and JSON
* running both definitions and comparing the results
* reusing the same provider setup for both paths

## Flow

```mermaid
flowchart LR
    A[greet] --> B[translate]
```

## Run it

Set your API key:

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

Then run:

```bash
cd samples/JsonVsCode
dotnet run
```

## What happens

The sample runs the same workflow twice:

* **Path A** builds the workflow in C#
* **Path B** loads the workflow from `workflow.json`

Both workflows:

* greet the input name
* pass that greeting to the next node
* translate it to French

## Example output

```text
═══ PATH A: Code-first (WorkflowBuilder) ═══

── Result A ──────────────────────────────────────────
  Greeting  : Hello, Spectra! It's great to connect with you!
  Translated: Bonjour, Spectra ! C'est formidable de te rencontrer !
  Errors    : 0

═══ PATH B: JSON-first (workflow.json) ═══

── Result B ──────────────────────────────────────────
  Greeting  : Hello, Spectra! It's great to connect with you!
  Translated: Bonjour, Spectra ! C'est super de te parler !
  Errors    : 0

═══ COMPARISON ═══

  Both workflows have the same structure:
  Code nodes: 2, JSON nodes: 2
  Code edges: 1, JSON edges: 1
  Code agents: 1, JSON agents: 1
  Both produced output: True
```

## Why the responses are slightly different

The workflow structure is the same in both cases, but LLM output is still non-deterministic.

That means:

* the greeting may be phrased slightly differently
* the French translation may also vary
* the important part is that both workflows follow the same graph and both succeed

## Code vs JSON

### Code-first

Use C# when you want to define workflows directly in your application code.

```csharp
var codeWorkflow = WorkflowBuilder.Create("greet-and-translate")
    .WithName("Greet and Translate")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("You are a helpful assistant. Keep responses short.")
        .WithMaxTokens(200))
    .AddAgentNode("greet", "assistant", node => node
        .WithUserPrompt("Say hello to {{inputs.name}} in one sentence.")
        .WithMaxIterations(1))
    .AddAgentNode("translate", "assistant", node => node
        .WithUserPrompt("Translate this to French: {{Context.greet.response}}")
        .WithMaxIterations(1))
    .AddEdge("greet", "translate")
    .Build();
```

### JSON-first

Use JSON when you want workflow definitions outside application code.

```json
{
  "id": "greet-and-translate",
  "name": "Greet and Translate",
  "entryNodeId": "greet",
  "nodes": [
    {
      "id": "greet",
      "stepType": "agent",
      "agentId": "assistant",
      "parameters": {
        "userPrompt": "Say hello to {{inputs.name}} in one sentence.",
        "maxIterations": 1
      }
    },
    {
      "id": "translate",
      "stepType": "agent",
      "agentId": "assistant",
      "parameters": {
        "userPrompt": "Translate this to French: {{Context.greet.response}}",
        "maxIterations": 1
      }
    }
  ],
  "edges": [
    { "from": "greet", "to": "translate" }
  ],
  "agents": [
    {
      "id": "assistant",
      "provider": "openrouter",
      "model": "openai/gpt-4o-mini",
      "systemPrompt": "You are a helpful assistant. Keep responses short.",
      "maxTokens": 200
    }
  ]
}
```

## When to use which

* use **code** when workflows are built as part of your app logic
* use **JSON** when workflows should be easier to store, edit, or load from files
* use either when you want the same Spectra execution model

Both are just different ways to create the same `WorkflowDefinition`.
