# LLM Resilience

Every LLM call can fail. Rate limits, overloaded servers, network drops, garbage responses from a weaker model — these aren't edge cases in production, they're the norm. Spectra wraps every provider client in a three-layer stack that handles all of it automatically.

The three layers are **retry**, **caching**, and **fallback**. Each one is a decorator that wraps an `ILlmClient`. They compose — cache sits outermost, retry wraps each individual provider call, fallback routes between providers when everything else fails.

---

## The Three Layers

### ResilientLlmClient — Retry & Timeout

`ResilientLlmClient` wraps a provider client and retries on transient failures — rate limits (HTTP 429), server errors (5xx), and network timeouts. Each attempt gets its own per-attempt timeout. Permanent errors like auth failures (401/403) or bad requests (400) fail immediately without retrying.

```
Request → ResilientLlmClient → Provider Client → LLM API
              │
              └── 429/5xx/timeout? → retry with exponential backoff
```

Configure it via `LlmResilienceOptions`:

| Option | Default | Description |
|--------|---------|-------------|
| `MaxRetries` | `3` | Retry attempts after the initial failure. Set to `0` to disable. |
| `BaseDelay` | `1s` | Starting delay. Doubles each attempt when exponential backoff is on. |
| `MaxDelay` | `30s` | Upper bound on retry delay. |
| `Timeout` | `60s` | Per-attempt wall-clock limit. |
| `UseExponentialBackoff` | `true` | When `true`: `delay = 2^(attempt-1) × baseDelay + 25% jitter`. |
| `RetryableStatusCodes` | `429, 500–504` | Which HTTP errors are considered transient. |

With defaults, the delay pattern looks like this:

| Attempt | Delay (approx) |
|---------|----------------|
| 1 (initial) | — |
| 2 (retry 1) | 1.0 – 1.25s |
| 3 (retry 2) | 2.0 – 2.5s |
| 4 (retry 3) | 4.0 – 5.0s |

The jitter prevents multiple workflows from hammering the provider at the same instant after a rate-limit window resets.

See [Retry & Timeout](../resilience/retry.md) for full documentation.

---

### CachingLlmClient — Response Caching

`CachingLlmClient` caches LLM responses so identical requests return instantly without hitting the API. The cache key is a deterministic SHA-256 hash of everything semantically relevant to the request: model name, all messages, temperature, max tokens, system prompt, output mode, and tool names.

```
Request → CachingLlmClient
              │
              ├── cache hit?  → return immediately (no API call)
              └── cache miss? → call inner client → cache the result
```

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `true` | Master switch. `false` = transparent pass-through. |
| `DefaultTtl` | `null` | Time-to-live. `null` means entries never expire. |
| `SkipWhenToolCalls` | `true` | Don't cache tool-calling responses — tool results depend on external state. |
| `SkipWhenMedia` | `true` | Don't cache image/audio/video requests. |

Built-in steps like `AgentStep` and `SessionStep` set `SkipCache = true` automatically — agentic loops always need a fresh response.

The default `InMemoryCacheStore` is suitable for development. For production, implement `ICacheStore` backed by Redis or any distributed cache.

See [Response Caching](../resilience/caching.md) for full documentation.

---

### FallbackLlmClient — Provider Fallback

`FallbackLlmClient` routes requests across multiple providers using one of four strategies. If a provider fails or its response doesn't pass a quality gate, the next one is tried.

```
Request → FallbackLlmClient
              │
              ├── try primary provider
              │     └── failed or quality gate rejected?
              ├── try next provider
              │     └── failed?
              └── try next... → exhausted → return error
```

The four routing strategies:

| Strategy | Behaviour |
|----------|-----------|
| `Failover` | Try providers in order. Move to the next only on failure. |
| `RoundRobin` | Rotate the starting provider on each request. On failure, cascade. |
| `Weighted` | Select probabilistically by weight. On failure, cascade. |
| `Split` | Deterministic bucket-based split — exact percentages. |

Each fallback chain can have a **quality gate** — a validator that inspects the response before accepting it. The built-in `MinLengthQualityGate` rejects responses that are too short (empty, truncated, or single-word). Implement `IQualityGate` to add domain-specific checks like JSON format validation.

See [Provider Fallback](../resilience/fallback.md) for full documentation.

---

## How the Stack Fits Together

The three decorators compose into a single chain. The ordering is important:

```
Request
  → CachingLlmClient          (check cache first — skip everything on hit)
      → FallbackLlmClient     (choose which provider to try)
          → ResilientLlmClient  (retry that provider on transient failure)
              → Provider Client   (actual API call)
```

Cache sits outermost: a cache hit skips the fallback and retry logic entirely. Fallback sits in the middle: it decides which provider to try next. Retry sits innermost: it retries a single provider before giving up and telling the fallback to move on.

This means a single request might look like:

1. Cache miss — proceed.
2. Fallback picks OpenAI as the primary.
3. Resilient client calls OpenAI — gets a 429.
4. Resilient client retries after 1s — gets a 429 again.
5. Resilient client exhausts retries — returns failure to fallback.
6. Fallback moves to Anthropic. Resilient client calls Anthropic — succeeds.
7. Response passes quality gate. CachingLlmClient stores the result.
8. Response returned.

---

## When Each Layer Activates

| Scenario | Cache | Retry | Fallback |
|----------|-------|-------|----------|
| Cache hit | Returns immediately | Not called | Not called |
| Transient 429 / 5xx | Miss | Retries with backoff | Only if all retries exhausted |
| Network timeout | Miss | Retries | Only if all retries exhausted |
| Provider fully down | Miss | Exhausts retries | Switches to next provider |
| Response too short (quality gate) | Miss | Not retried | Switches to next provider |
| Auth error (401/403) | Miss | Not retried (fail fast) | Switches to next provider |
| Agentic loop (AgentStep) | Skipped (`SkipCache`) | Active | Active |

---

## Configuring Per Agent

Resilience options are per-agent, not global. A cheap draft agent used for classification doesn't need the same retry budget as an expensive reasoning agent.

```csharp
services.AddSpectra(builder =>
{
    builder.AddOpenAi(c => { c.ApiKey = openAiKey; });
    builder.AddAnthropic(c => { c.ApiKey = anthropicKey; });

    // Reasoning agent — aggressive retry, long timeout
    builder.AddAgent("reasoner", "openai", "gpt-4o", agent => agent
        .WithResilienceOptions(new LlmResilienceOptions
        {
            MaxRetries = 5,
            Timeout = TimeSpan.FromSeconds(120)
        }));

    // Draft agent — fast fail, no retries
    builder.AddAgent("drafter", "openai", "gpt-4o-mini", agent => agent
        .WithResilienceOptions(new LlmResilienceOptions
        {
            MaxRetries = 0,
            Timeout = TimeSpan.FromSeconds(15)
        }));

    // Fallback policy — production multi-provider chain
    builder.AddFallbackPolicy("production",
        strategy: FallbackStrategy.Failover,
        entries: new[]
        {
            new FallbackProviderEntry { Provider = "openai", Model = "gpt-4o" },
            new FallbackProviderEntry { Provider = "anthropic", Model = "claude-sonnet-4-20250514" }
        },
        defaultQualityGate: new MinLengthQualityGate(20));
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