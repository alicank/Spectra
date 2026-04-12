---
description: "Route LLM requests across multiple providers in Spectra using fallback strategies and quality gates."
---

# Provider Fallback

Fallback lets Spectra route one logical LLM request across multiple providers or models.

Use it when you want:

- a backup provider if the primary fails
- load distribution across providers
- gradual rollout to a new model
- quality checks before accepting a fallback response

A fallback policy defines:

- which providers are in the group
- which routing strategy to use
- which quality gate to apply

---

## When to use fallback

Fallback is useful in three common situations:

| Goal | Example |
| --- | --- |
| **Resilience** | Try Anthropic if OpenAI fails |
| **Traffic distribution** | Spread requests across several providers |
| **Migration or experimentation** | Send part of traffic to a new model |

---

## Define a fallback policy

A fallback policy is a named provider group with a routing strategy.

```csharp
builder.AddFallbackPolicy("resilient",
    strategy: FallbackStrategy.Failover,
    entries: new[]
    {
        new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o" },
        new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet-4-20250514" },
        new FallbackProviderEntry { Provider = "ollama", Model = "llama3" }
    },
    defaultQualityGate: new MinLengthQualityGate(50));
```

In this example:

- Spectra tries providers using the selected strategy
- if one fails, it moves to the next candidate
- if a response fails the quality gate, it is rejected and Spectra continues

---

## Choose a strategy

| Strategy | How it works | Best for |
| --- | --- | --- |
| `Failover` | Try providers in order until one succeeds | Clear primary/backup preference |
| `RoundRobin` | Rotate which provider is tried first | Even distribution |
| `Weighted` | Choose the starting provider probabilistically by weight | Gradual rollout or cost tuning |
| `Split` | Bucket requests deterministically by percentage | Predictable traffic splits |

### Failover

```csharp
FallbackStrategy.Failover
```

Spectra tries providers in order.

Example:

- OpenAI
- then Anthropic if OpenAI fails
- then Ollama if Anthropic fails

Use this when you have a clear preferred provider and only want backups when needed.

### Round robin

```csharp
FallbackStrategy.RoundRobin
```

Each new request starts with the next provider in the list.

Example:

- request 1 starts with OpenAI
- request 2 starts with Anthropic
- request 3 starts with Ollama

Use this when you want simple load spreading across providers.

### Weighted

```csharp
FallbackStrategy.Weighted
```

The starting provider is selected probabilistically from the configured weights.

Example:

- OpenAI weight 70
- Anthropic weight 30

Over time, about 70% of requests start with OpenAI and 30% with Anthropic.

Use this for gradual migration or cost-based routing.

### Split

```csharp
FallbackStrategy.Split
```

Requests are assigned to providers deterministically by configured percentages.

Example:

- OpenAI handles requests 1–70
- Anthropic handles requests 71–100
- then the cycle repeats

Use this when you want predictable traffic buckets instead of probabilistic selection.

---

## Provider entries

Each provider in the policy can define its own routing and quality settings.

```csharp
new FallbackProviderEntry
{
    Provider = "openai",
    Model = "gpt-4o",
    Weight = 70,
    QualityGate = new MinLengthQualityGate(100),
    MaxRequestsPerMinute = 500
}
```

### Entry fields

| Field | Purpose |
| --- | --- |
| `Provider` | Registered provider name |
| `Model` | Model to use for this entry |
| `Weight` | Used by `Weighted` and `Split` |
| `QualityGate` | Per-entry quality validation |
| `MaxRequestsPerMinute` | Skip this entry when its rate budget is exhausted |

---

## Quality gates

A quality gate checks whether a provider response is good enough before Spectra accepts it.

This is useful when fallback chains include weaker or cheaper models and you want to avoid silently serving poor results.

### `IQualityGate`

```csharp
public interface IQualityGate
{
    QualityGateResult Evaluate(LlmResponse response);
}
```

### Built-in gates

#### `MinLengthQualityGate`

Rejects responses shorter than a minimum length.

```csharp
new MinLengthQualityGate(minimumLength: 50)
```

Useful for rejecting empty, truncated, or clearly incomplete responses.

#### `CompositeQualityGate`

Combines multiple gates. All must pass.

```csharp
new CompositeQualityGate(
    new MinLengthQualityGate(50),
    new MyCustomFormatGate()
)
```

### Custom quality gate

```csharp
public class JsonFormatGate : IQualityGate
{
    public QualityGateResult Evaluate(LlmResponse response)
    {
        try
        {
            JsonDocument.Parse(response.Content);
            return QualityGateResult.Pass();
        }
        catch
        {
            return QualityGateResult.Fail("Response is not valid JSON.");
        }
    }
}
```

Use a custom gate when your application needs a specific output format or domain-level validation.

---

## What happens when a provider fails

When a provider attempt fails, Spectra tries the next provider based on the policy strategy.

When a response is returned but fails the quality gate, Spectra also rejects it and moves on.

If every provider fails or is rejected, the fallback client fails the request.

---

## Events

Fallback behavior emits events for observability.

| Event | When |
| --- | --- |
| `FallbackTriggeredEvent` | Spectra moves from one provider to the next |
| `QualityGateRejectedEvent` | A response is rejected by a quality gate |
| `FallbackExhaustedEvent` | All providers in the chain have been exhausted |

These are useful for logs, dashboards, and reliability monitoring.

---

## Full example

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(c => { c.ApiKey = openAiKey; });
    builder.AddAnthropic(c => { c.ApiKey = anthropicKey; });
    builder.AddOllama(c => { c.Model = "llama3"; });

    builder.AddFallbackPolicy("production",
        strategy: FallbackStrategy.Failover,
        entries: new[]
        {
            new FallbackProviderEntry
            {
                Provider = "openai",
                Model = "gpt-4o",
                MaxRequestsPerMinute = 500
            },
            new FallbackProviderEntry
            {
                Provider = "anthropic",
                Model = "claude-sonnet-4-20250514",
                MaxRequestsPerMinute = 300
            },
            new FallbackProviderEntry
            {
                Provider = "ollama",
                Model = "llama3"
            }
        },
        defaultQualityGate: new MinLengthQualityGate(20));
});
```

This setup gives you:

- OpenAI as the preferred provider
- Anthropic as the first backup
- Ollama as a final fallback
- basic response quality validation

---

## A simple mental model

Fallback answers one question:

**If this provider should not serve the response, who should try next?**

That "should not" can mean:

- the provider failed
- the provider hit a rate limit
- the response was not good enough

---

## What's next?

<div class="grid cards" markdown>

- **Retry & Timeout**

  Retry transient failures before moving on to another provider.

  [:octicons-arrow-right-24: Retry](retry.md)

- **Caching**

  Avoid duplicate LLM requests when the same input repeats.

  [:octicons-arrow-right-24: Caching](caching.md)

</div>