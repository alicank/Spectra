# LLM Resilience

Every LLM call can fail. Rate limits, overloaded servers, network drops, garbage responses from a weaker model - these aren't edge cases in production, they're the norm. Spectra provides resilience building blocks for provider calls, and LLM steps can route through named fallback policies.

The three building blocks are **retry**, **caching**, and **fallback**. Retry and caching are `ILlmClient` decorators you can compose around a client. Fallback is wired into `PromptStep`, `StructuredOutputStep`, and `AgentStep` through the `fallbackPolicy` input.

---

## The Three Building Blocks

### ResilientLlmClient - Retry & Timeout

`ResilientLlmClient` wraps a provider client and retries on transient failures - rate limits (HTTP 429), server errors (5xx), and network timeouts. Each attempt gets its own per-attempt timeout. Permanent errors like auth failures (401/403) or bad requests (400) fail immediately without retrying.

```
Request -> ResilientLlmClient -> Provider Client -> LLM API
              |
              +-- 429/5xx/timeout? -> retry with exponential backoff
```

Configure it via `LlmResilienceOptions`:

| Option | Default | Description |
|--------|---------|-------------|
| `MaxRetries` | `3` | Retry attempts after the initial failure. Set to `0` to disable. |
| `BaseDelay` | `1s` | Starting delay. Doubles each attempt when exponential backoff is on. |
| `MaxDelay` | `30s` | Upper bound on retry delay. |
| `Timeout` | `60s` | Per-attempt wall-clock limit. |
| `UseExponentialBackoff` | `true` | When `true`: `delay = 2^(attempt-1) * baseDelay + 25% jitter`. |
| `RetryableStatusCodes` | `429, 500-504` | Which HTTP errors are considered transient. |

With defaults, the delay pattern looks like this:

| Attempt | Delay (approx) |
|---------|----------------|
| 1 (initial) | none |
| 2 (retry 1) | 1.0 - 1.25s |
| 3 (retry 2) | 2.0 - 2.5s |
| 4 (retry 3) | 4.0 - 5.0s |

The jitter prevents multiple workflows from hammering the provider at the same instant after a rate-limit window resets.

See [Retry & Timeout](../resilience/retry.md) for full documentation.

---

### CachingLlmClient - Response Caching

`CachingLlmClient` caches LLM responses so identical requests return instantly without hitting the API. The cache key is a deterministic SHA-256 hash of everything semantically relevant to the request: model name, all messages, temperature, max tokens, stop sequence, system prompt, output mode, JSON schema, and tool names.

```
Request -> CachingLlmClient
              |
              +-- cache hit?  -> return immediately (no API call)
              +-- cache miss? -> call inner client -> cache the result
```

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `true` | Master switch. `false` = transparent pass-through. |
| `DefaultTtl` | `null` | Time-to-live. `null` means entries never expire. |
| `SkipWhenToolCalls` | `true` | Don't cache tool-calling responses - tool results depend on external state. |
| `SkipWhenMedia` | `true` | Don't cache image/audio/video requests. |

Built-in steps like `AgentStep` and `SessionStep` set `SkipCache = true` automatically - agentic loops always need a fresh response.

The built-in `InMemoryCacheStore` is suitable for development. For production, implement `ICacheStore` backed by Redis or any distributed cache.

See [Response Caching](../resilience/caching.md) for full documentation.

---

### FallbackLlmClient - Provider Fallback

`FallbackLlmClient` routes requests across multiple providers using one of four strategies. If a provider fails or its response doesn't pass a quality gate, the next one is tried.

```
Request -> FallbackLlmClient
              |
              +-- try primary provider
              |     +-- failed or quality gate rejected?
              +-- try next provider
              |     +-- failed?
              +-- try next... -> exhausted -> return error
```

The four routing strategies:

| Strategy | Behaviour |
|----------|-----------|
| `Failover` | Try providers in order. Move to the next only on failure. |
| `RoundRobin` | Rotate the starting provider on each request. On failure, cascade. |
| `Weighted` | Select probabilistically by weight. On failure, cascade. |
| `Split` | Deterministic bucket-based split by weight. |

Each fallback chain can have a **quality gate** - a validator that inspects the response before accepting it. The built-in `MinLengthQualityGate` rejects responses that are too short (empty, truncated, or single-word). Implement `IQualityGate` to add domain-specific checks like JSON format validation.

See [Provider Fallback](../resilience/fallback.md) for full documentation.

---

## How the Pieces Fit Together

Today, provider fallback is the piece connected to the built-in LLM steps. A request with a `fallbackPolicy` input looks like this:

```
PromptStep / AgentStep / StructuredOutputStep
  -> FallbackLlmClient
      -> selected provider client
```

Retry and caching are available as standalone client decorators. If you construct clients manually, you can wrap them yourself:

```
Request
  -> CachingLlmClient
      -> ResilientLlmClient
          -> Provider Client
```

The full cache -> fallback -> retry stack described here is a useful composition pattern, but it is not automatically assembled by `AddSpectra` today.

---

## When Each Piece Activates

| Scenario | Cache | Retry | Fallback |
|----------|-------|-------|----------|
| Cache hit | Returns immediately when you use `CachingLlmClient` | Not called | Not called |
| Transient 429 / 5xx | Miss | Retries when you use `ResilientLlmClient` | Built-in steps switch only if using `fallbackPolicy` |
| Network timeout | Miss | Retries when you use `ResilientLlmClient` | Built-in steps switch only if using `fallbackPolicy` |
| Provider fully down | Miss | Exhausts retries when wrapped | Switches to next provider when using `fallbackPolicy` |
| Response too short (quality gate) | Miss | Not retried | Switches to next provider when using `fallbackPolicy` |
| Auth error (401/403) | Miss | Not retried (fail fast) | Switches to next provider when using `fallbackPolicy` |
| Agentic loop (`AgentStep`) | Skipped (`SkipCache`) if a caching decorator is present | Depends on whether the client is wrapped | Active when `fallbackPolicy` is configured |

---

## Configuring Fallback Policies

Fallback is configured as a named policy and attached to an LLM node with the `fallbackPolicy` parameter.

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(c => { c.ApiKey = openAiKey; });
    builder.AddAnthropic(c => { c.ApiKey = anthropicKey; });

    builder.AddFallbackPolicy("production",
        strategy: FallbackStrategy.Failover,
        entries:
        [
            new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o" },
            new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet-4-20250514" }
        ],
        defaultQualityGate: new MinLengthQualityGate(20));
});

var workflow = WorkflowBuilder.Create("resilient-summary")
    .AddAgent("summarizer", "openai", "gpt-4o")
    .AddNode("summarize", "prompt", node => node
        .WithAgent("summarizer")
        .WithParameter("fallbackPolicy", "production")
        .WithParameter("userPrompt", "Summarize: {{inputs.text}}"))
    .Build();
```

For direct `ILlmClient` usage outside the built-in steps, construct the decorators explicitly:

```csharp
var resilient = new ResilientLlmClient(rawClient, new LlmResilienceOptions
{
    MaxRetries = 5,
    Timeout = TimeSpan.FromSeconds(120)
});

var cached = new CachingLlmClient(resilient, cacheStore, new LlmCacheOptions
{
    DefaultTtl = TimeSpan.FromMinutes(30)
});
```

---

## What's Next

<div class="grid cards" markdown>

-   **Retry & Timeout**

    Exponential backoff, per-attempt timeouts, and retryable status codes.

    [:octicons-arrow-right-24: Retry](../resilience/retry.md)

-   **Provider Fallback**

    Multi-provider routing, weighted strategies, and quality gates.

    [:octicons-arrow-right-24: Fallback](../resilience/fallback.md)

-   **Response Caching**

    Cache key generation, TTL, and custom cache stores.

    [:octicons-arrow-right-24: Caching](../resilience/caching.md)

</div>
