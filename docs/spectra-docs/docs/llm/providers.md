---
description: "Connect Spectra to OpenAI, Anthropic, Gemini, Ollama, OpenRouter, or any OpenAI-compatible endpoint."
---

# LLM Providers

A **provider** is how Spectra connects to a model backend.

It tells Spectra **where** to send LLM requests:

- OpenAI
- Anthropic
- Gemini
- Ollama
- OpenRouter
- or any OpenAI-compatible API

Your workflows do not need to change when you switch vendors.

In Spectra, the usual pattern is:

- a **provider** connects to an API
- an **agent** chooses which provider and model to use
- a **workflow** uses that agent inside nodes

That separation makes it easy to change models without rewriting workflow logic.

---

## What this page covers

In this guide, you will learn:

1. what a provider is
2. how to register a provider
3. how to register multiple providers
4. how agents choose a provider and model
5. how Spectra handles capabilities like streaming and tool calling

If you only want the fast path, start with one provider example below and then jump to [Agent Definitions](#agent-definitions).

---

## Choosing a provider

Most teams start with one of these setups:

- **OpenAI** if you want the most familiar hosted API experience
- **Anthropic** if you want Claude models directly
- **Gemini** if you use Google's model ecosystem
- **Ollama** if you want local models on your machine or server
- **OpenRouter** if you want one API that can route to many vendors
- **OpenAI-compatible** if your vendor exposes the OpenAI API shape

You can register one provider or several at the same time.

---

## Register a provider

Provider registration usually happens during application startup inside `AddSpectra(...)`.

### OpenAI

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        config.Model = "gpt-4o";
    });
});
```

Use OpenAI when you want direct access to OpenAI models through the standard API.

### Anthropic

```csharp
services.AddSpectra(builder =>
{
    builder.AddAnthropic(config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!;
        config.Model = "claude-sonnet-4-20250514";
    });
});
```

Use Anthropic when you want Claude models directly.

### Gemini

```csharp
services.AddSpectra(builder =>
{
    builder.AddGemini(config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")!;
        config.Model = "gemini-2.0-flash";
    });
});
```

Use Gemini when you want Google's hosted models.

### Ollama

```csharp
services.AddSpectra(builder =>
{
    builder.AddOllama(config =>
    {
        config.Host = "http://localhost:11434";
        config.Model = "llama3";
    });
});
```

Use Ollama when you want local inference without sending requests to a hosted API.

### OpenRouter

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenRouter(config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;
        config.Model = "openai/gpt-4o";
    });
});
```

Use OpenRouter when you want one API key that can access models from multiple vendors.

### Any OpenAI-compatible endpoint

```csharp
services.AddSpectra(builder =>
{
    builder.AddProvider("deepseek", config =>
    {
        config.ApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")!;
        config.BaseUrl = "https://api.deepseek.com/v1";
        config.Model = "deepseek-chat";
    });
});
```

Use this when your provider follows the OpenAI API shape.

The first argument, such as `"deepseek"`, becomes the provider name that agents can reference later.

---

## Register multiple providers

A common setup is to register more than one provider and let different agents use different models.

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(c =>
    {
        c.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        c.Model = "gpt-4o";
    });

    builder.AddAnthropic(c =>
    {
        c.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!;
        c.Model = "claude-sonnet-4-20250514";
    });

    builder.AddOllama(c =>
    {
        c.Host = "http://localhost:11434";
        c.Model = "llama3";
    });
});
```

This is useful when you want:

- one high-quality model for planning
- one fast or cheap model for drafting
- one local model for development or private workloads

---

## Provider vs agent

This is the key distinction:

### Provider

A provider defines **how Spectra talks to a backend**.

Examples:

- `"openai"`
- `"anthropic"`
- `"gemini"`
- `"ollama"`
- `"openrouter"`
- `"deepseek"` (custom OpenAI-compatible registration)

### Agent

An agent defines **which provider and model a workflow node should use**.

Examples:

- planner agent uses OpenAI
- reviewer agent uses Anthropic
- local-dev agent uses Ollama

So the provider is the connection, while the agent is the named LLM identity inside your workflow.

---

## Agent definitions

An **agent** binds a provider to a model and optional behavior such as system prompt, temperature, or token limits.

### Minimum definition

Every agent needs three values:

```csharp
builder.AddAgent("researcher", "openai", "gpt-4o");
//                 ↑ id          ↑ provider  ↑ model
```

### With more configuration

```csharp
builder.AddAgent("researcher", "openai", "gpt-4o", agent => agent
    .WithTemperature(0.3)
    .WithMaxTokens(4096)
    .WithSystemPrompt("You are a research assistant."));
```

### Example with multiple agents

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(c =>
    {
        c.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        c.Model = "gpt-4o";
    });

    builder.AddAnthropic(c =>
    {
        c.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!;
        c.Model = "claude-sonnet-4-20250514";
    });

    builder.AddAgent("planner", "openai", "gpt-4o", agent => agent
        .WithSystemPrompt("You are a project planner."));

    builder.AddAgent("reviewer", "anthropic", "claude-sonnet-4-20250514", agent => agent
        .WithSystemPrompt("You review work critically and clearly."));
});
```

A workflow can now reference `"planner"` or `"reviewer"` without caring about API details.

---

## Where agents can be registered

Spectra supports three main registration paths:

| Method | Scope | Use Case |
| --- | --- | --- |
| `builder.AddAgent(...)` | Global | Shared across the application |
| `WorkflowBuilder.AddAgent(...)` | Workflow | Scoped to one workflow definition |
| `builder.AddAgentsFromDirectory("./agents")` | Global | Load agent files from disk |

This gives you flexibility in how you organize model configuration.

---

## How provider resolution works

When a step needs an LLM client, Spectra resolves it in a simple way:

### Path 1: by agent ID

Most workflows use an agent ID.

Spectra:

1. finds the agent definition
2. reads its provider and model
3. asks that provider to create a client

### Path 2: ad-hoc provider and model

Some steps can specify a provider and model directly at runtime.

In that case, Spectra creates a temporary agent-like configuration and resolves the client the same way.

### Fallback behavior

If a named provider is not found directly, Spectra can scan registered providers and check whether one reports support for the requested model.

For most users, the important takeaway is simple:

- define providers once
- define agents once
- reference agents from workflows

---

## Provider-specific configuration

Most users only need `ApiKey`, `Model`, and sometimes `BaseUrl`.

Below are the main options for each built-in provider.

### OpenAI

```csharp
builder.AddOpenAi(config =>
{
    config.ApiKey = "sk-...";
    config.Model = "gpt-4o";
    config.BaseUrl = "https://api.openai.com/v1";
});
```

| Config | Default | Description |
| --- | --- | --- |
| `ApiKey` | — | Authentication token |
| `Model` | `"gpt-4o"` | Default model if the agent does not override it |
| `BaseUrl` | `https://api.openai.com/v1` | Base API URL |
| `ApiVersion` | — | Mainly used for Azure OpenAI |
| `Organization` | — | OpenAI organization header |

#### Azure OpenAI

```csharp
builder.AddOpenAi(config =>
{
    config.BaseUrl = "https://my-resource.openai.azure.com/openai/deployments/gpt4";
    config.ApiKey = "azure-key-...";
    config.ApiVersion = "2024-02-01";
});
```

### Anthropic

```csharp
builder.AddAnthropic(config =>
{
    config.ApiKey = "sk-ant-...";
    config.Model = "claude-sonnet-4-20250514";
});
```

| Config | Default | Description |
| --- | --- | --- |
| `ApiKey` | — | API key |
| `Model` | `"claude-sonnet-4-20250514"` | Default model |
| `BaseUrl` | `https://api.anthropic.com/v1` | Base API URL |
| `AnthropicVersion` | `"2023-06-01"` | API version header |

### Gemini

```csharp
builder.AddGemini(config =>
{
    config.ApiKey = "AIza...";
    config.Model = "gemini-2.0-flash";
});
```

| Config | Default | Description |
| --- | --- | --- |
| `ApiKey` | — | API key |
| `Model` | `"gemini-2.0-flash"` | Default model |
| `BaseUrl` | `https://generativelanguage.googleapis.com/v1beta` | Base API URL |

### Ollama

```csharp
builder.AddOllama(config =>
{
    config.Host = "http://localhost:11434";
    config.Model = "llama3";
    config.KeepAlive = "5m";
});
```

| Config | Default | Description |
| --- | --- | --- |
| `Host` | `http://localhost:11434` | Ollama server URL |
| `Model` | `"llama3"` | Default model |
| `KeepAlive` | — | How long the model stays loaded |
| `Options` | `{}` | Raw Ollama generation options |

### OpenRouter

```csharp
builder.AddOpenRouter(config =>
{
    config.ApiKey = "sk-or-...";
    config.Model = "openai/gpt-4o";
    config.SiteName = "My App";
});
```

| Config | Default | Description |
| --- | --- | --- |
| `ApiKey` | — | Authentication token |
| `Model` | `"openai/gpt-4o"` | Default model |
| `BaseUrl` | `https://openrouter.ai/api/v1` | Base API URL |
| `SiteUrl` | — | Sent as `HTTP-Referer` |
| `SiteName` | — | Sent as `X-Title` |

---

## Model capabilities

Every provider config exposes a `Capabilities` property that lets you tell Spectra what a model supports. Each provider has its own capabilities config type (`ModelCapabilitiesConfig` for OpenAI/OpenAI-compatible, `AnthropicCapabilitiesConfig`, `GeminiCapabilitiesConfig`, `OllamaCapabilitiesConfig`, `OpenRouterCapabilitiesConfig`), but they all expose the same fields.

Spectra sets sensible defaults for each provider. Override only when a specific model differs from the provider default — for example, an older model that does not support tool calling.

```csharp
// OpenAI / OpenAI-compatible
builder.AddOpenAi(config =>
{
    config.Capabilities = new ModelCapabilitiesConfig
    {
        SupportsJsonMode = true,
        SupportsToolCalling = true,
        SupportsVision = true,
        SupportsStreaming = true,
        MaxContextTokens = 128_000,
        MaxOutputTokens = 16_384
    };
});

// Anthropic
builder.AddAnthropic(config =>
{
    config.Capabilities = new AnthropicCapabilitiesConfig
    {
        SupportsToolCalling = true,
        SupportsStreaming = true,
        MaxContextTokens = 200_000,
        MaxOutputTokens = 8_192
    };
});
```

| Capability | Description |
| --- | --- |
| `SupportsJsonMode` | The model can be guided through native JSON output modes |
| `SupportsToolCalling` | The model can call tools or functions |
| `SupportsVision` | The model can handle image input |
| `SupportsStreaming` | The model supports streamed output |
| `SupportsAudio` / `SupportsVideo` | The model supports richer multimodal input |
| `MaxContextTokens` / `MaxOutputTokens` | Token limits for input and output |

For example, if JSON mode is not supported, Spectra can fall back to prompt-based structured output strategies.

---

## Under the hood

Most users do not need the provider contracts directly.

But if you want to understand how providers plug into Spectra, the core interfaces look like this:

```csharp
public interface ILlmProvider
{
    string Name { get; }
    ILlmClient CreateClient(AgentDefinition agent);
    bool SupportsModel(string modelId);
}

public interface ILlmClient
{
    string ProviderName { get; }
    string ModelId { get; }
    ModelCapabilities Capabilities { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

public interface ILlmStreamClient : ILlmClient
{
    IAsyncEnumerable<string> StreamAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
```

At a high level:

- `ILlmProvider` creates clients
- `ILlmClient` handles normal completion calls
- `ILlmStreamClient` adds streaming support

If you want to implement your own backend integration, see the custom provider guide.

---

## Common setup patterns

### One provider for everything

Good for small projects and first adoption.

- easiest setup
- minimal operational complexity
- one billing and auth path

### Multiple providers for specialized agents

Good when different tasks need different model behavior.

Examples:

- planning with a stronger model
- drafting with a cheaper model
- local dev with Ollama

### OpenRouter as a single entry point

Good when you want flexibility without registering many providers separately.

---

## Common issues

### Provider not found

Make sure the provider name used by the agent matches the name you registered.

```csharp
builder.AddAgent("researcher", "openai", "gpt-4o");
```

That agent expects an OpenAI provider registration.

### API key is missing

Check that the environment variable exists in the same process where your app is running.

### Wrong base URL

Use the provider root URL, not a final endpoint like `/chat/completions` or `/messages`, unless the provider specifically expects that shape.

### Model name mismatch

Make sure the model format matches the provider. For example, OpenRouter usually uses `vendor/model` identifiers.

---

## What's next?

<div class="grid cards" markdown>

- **Prompts**

  Define reusable system and user prompts for your agents.

  [:octicons-arrow-right-24: Prompts](prompts.md)

- **Prompt Steps**

  Use providers and agents inside workflow nodes.

  [:octicons-arrow-right-24: Prompt Steps](prompt-steps.md)

- **Resilience**

  Add retry, fallback, timeout, and caching behavior around model calls.

  [:octicons-arrow-right-24: Retry & Timeout](../resilience/retry.md)

- **Custom Provider**

  Implement your own provider integration for another backend.

  [:octicons-arrow-right-24: Custom Provider Guide](../guides/custom-provider.md)

</div>