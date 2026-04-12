---
description: "Use PromptStep for text generation and StructuredOutputStep for JSON output in Spectra workflows."
---

# Prompt & Structured Output Steps

Use these steps when you want to call an LLM directly inside a workflow.

- **`PromptStep`** for normal text output
- **`StructuredOutputStep`** for JSON output you want to consume downstream

| Need | Use |
| --- | --- |
| Generate text | `PromptStep` |
| Generate JSON | `StructuredOutputStep` |

---

## `PromptStep`

`PromptStep` performs one LLM call and returns the response.

**StepType:** `"prompt"`

Use it for:

- summarization
- translation
- rewriting
- drafting
- one-shot LLM tasks

### Basic usage

=== "Builder API"

    ```csharp
    var workflow = Spectra.Workflow("summarize")
        .AddPromptStep("summarize", agent: "openai",
            prompt: "Summarize this in 3 bullets: {{inputs.text}}")
        .Build();
    ```

=== "JSON Workflow"

    ```json
    {
      "nodes": [
        {
          "id": "summarize",
          "stepType": "prompt",
          "agent": "openai",
          "inputs": {
            "userPrompt": "Summarize this in 3 bullets: {{inputs.text}}"
          }
        }
      ]
    }
    ```

### Common inputs

| Input | Type | Default | Description |
| --- | --- | --- | --- |
| `userPrompt` | `string` | — | Inline user prompt |
| `userPromptRef` | `string` | — | Prompt ID from the prompt registry |
| `systemPrompt` | `string` | — | Inline system prompt |
| `systemPromptRef` | `string` | — | System prompt ID from the prompt registry |
| `agent` | `string` | — | Registered agent ID |
| `provider` | `string` | — | Provider name if no agent is used |
| `model` | `string` | `"unknown"` | Model ID |
| `temperature` | `double` | `0.7` | Sampling temperature |
| `maxTokens` | `int` | `2048` | Max response tokens |
| `images` | `list` | — | Images for multimodal requests |
| `messages` | `List<LlmMessage>` | — | Prebuilt messages; bypasses prompt resolution |
| `skipCache` | `bool` | `false` | Skip the LLM response cache |

### Prompt resolution

For `PromptStep`, Spectra resolves prompts in this order.

**User prompt**

1. `userPrompt`
2. `userPromptRef`
3. no user prompt

**System prompt**

1. `systemPrompt`
2. `systemPromptRef`
3. agent system prompt or prompt reference
4. no system prompt

All prompt templates support `{{...}}` expressions.

### Multimodal input

If the model supports vision, you can send images with the prompt:

```csharp
var workflow = Spectra.Workflow("describe-image")
    .AddPromptStep("describe", agent: "openai", inputs: new
    {
        userPrompt = "Describe what you see in this image.",
        images = new[]
        {
            new { data = base64ImageData, mimeType = "image/png" }
        }
    })
    .Build();
```

### Streaming

When the workflow runs in streaming mode, `PromptStep` automatically streams tokens if the client supports it.

!!! note
    Streaming is only used for token delivery during execution. The final full response is still captured in the step outputs.

### Outputs

| Output | Type | Description |
| --- | --- | --- |
| `response` | `string` | Full LLM response |
| `model` | `string` | Model actually used |
| `inputTokens` | `int?` | Prompt token count |
| `outputTokens` | `int?` | Completion token count |
| `latency` | `TimeSpan?` | LLM round-trip time |
| `stopReason` | `string?` | Why generation stopped |
| `toolCalls` | `List<ToolCall>?` | Tool calls requested by the model |

In most workflows, the main value you use downstream is the response text.

---

## `StructuredOutputStep`

`StructuredOutputStep` is for cases where you want structured JSON instead of plain text.

**StepType:** `"structured_output"`

Use it for:

- extraction
- classification
- records or objects
- downstream machine-readable outputs

### Basic usage

```csharp
var workflow = Spectra.Workflow("extract-entities")
    .AddStep("extract", new StructuredOutputStep(providerRegistry, agentRegistry, promptRenderer),
        inputs: new
        {
            agent = "openai",
            userPrompt = "Extract all people and places from: {{inputs.text}}",
            jsonSchema = """
            {
                "type": "object",
                "properties": {
                    "people": { "type": "array", "items": { "type": "string" } },
                    "places": { "type": "array", "items": { "type": "string" } }
                }
            }
            """
        })
    .Build();
```

### How it works

```mermaid
flowchart LR
    A[Resolve prompt] --> B[Call model in JSON mode]
    B --> C[Parse response as JSON]
    C --> D[Expose rawResponse and parsedResponse]
```

If `jsonSchema` is provided, Spectra uses structured JSON mode when the provider supports it.

If no schema is provided, it requests general JSON output.

If the response is not valid JSON, the step fails with the parse error and keeps the raw response.

### Outputs

`StructuredOutputStep` includes all `PromptStep` outputs, plus:

| Output | Type | Description |
| --- | --- | --- |
| `rawResponse` | `string` | Original LLM response before parsing |
| `parsedResponse` | `JsonElement` | Parsed JSON result |

### Consuming structured output downstream

```csharp
var workflow = Spectra.Workflow("extract-and-process")
    .AddStep("extract", new StructuredOutputStep(...), inputs: new { ... })
    .AddPromptStep("process", agent: "openai",
        prompt: "Write bios for: {{nodes.extract.output.parsedResponse}}")
    .Edge("extract", "process")
    .Build();
```

!!! warning "Schema support varies by provider"
    Not every provider supports native structured output. When native schema mode is unavailable, Spectra falls back to JSON-oriented prompting. You should still validate important outputs.

---

## Choosing between them

| Scenario | Use |
| --- | --- |
| Free-form text generation | `PromptStep` |
| Need JSON output for downstream logic | `StructuredOutputStep` |
| Have a schema and want structured JSON | `StructuredOutputStep` with `jsonSchema` |
| Need JSON but no fixed schema | `StructuredOutputStep` without `jsonSchema` |
| Need tool calling | `PromptStep` or `AgentStep` |

---

## What's next?

<div class="grid cards" markdown>

- **Prompts**

  Reuse prompt files and prompt references.

  [:octicons-arrow-right-24: Prompt Management](prompts.md)

- **Providers**

  Connect steps to OpenAI, Anthropic, Gemini, Ollama, OpenRouter, or compatible APIs.

  [:octicons-arrow-right-24: Providers](providers.md)

- **Agent Step**

  Use multi-turn agents with tools, loops, and handoffs.

  [:octicons-arrow-right-24: Agent Step](agent-step.md)

</div>