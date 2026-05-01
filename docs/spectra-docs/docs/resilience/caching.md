---
description: "Cache repeated LLM responses in Spectra to reduce latency, token usage, and cost."
---

# Response Caching

`CachingLlmClient` caches repeated LLM responses.

If the same request is made again, Spectra can return the cached result instead of calling the provider again.

This helps reduce:

- latency
- token usage
- provider cost

A simple rule:

- same request → same cache key
- same cache key → cached response

---

## What caching does

`CachingLlmClient` wraps an `ILlmClient` and checks the cache before making a real LLM call.

- if the request is already cached, return the cached response
- if not, call the provider and store the result

This works best for repeated, deterministic requests.

---

## Configuration

```csharp
var options = new LlmCacheOptions
{
    Enabled = true,
    DefaultTtl = TimeSpan.FromHours(1),
    KeyPrefix = "spectra:llm:",
    SkipWhenToolCalls = true,
    SkipWhenMedia = true
};
```

### Options

| Option | Default | Description |
| --- | --- | --- |
| `Enabled` | `true` | Turns caching on or off |
| `DefaultTtl` | `null` | How long entries live. `null` means no expiration |
| `KeyPrefix` | `"spectra:llm:"` | Prefix added to cache keys |
| `SkipWhenToolCalls` | `true` | Do not cache responses that contain tool calls |
| `SkipWhenMedia` | `true` | Do not cache requests with image, audio, or video input |

---

## When Spectra skips caching

Spectra does not cache every request.

By default, caching is skipped for:

- responses that contain tool calls
- requests with media input
- requests that explicitly set `SkipCache = true`

Built-in agentic steps such as `AgentStep` and `SessionStep` also use fresh responses by default, because their behavior depends on changing runtime context.

### Per-request skip

```csharp
var request = new LlmRequest
{
    Model = "gpt-4o",
    Messages = messages,
    SkipCache = true
};
```

Use this when you always want a fresh provider response.

---

## Cache keys

Spectra generates a deterministic cache key from the parts of the request that affect the response.

This includes things like:

- model
- messages
- temperature
- max tokens
- system prompt
- output mode
- JSON schema
- tool names

The key is generated from the semantic content of the request, not just raw object shape.

That means equivalent requests produce the same key even if the input ordering differs in unimportant ways.

Example key:

```text
spectra:llm:gpt-4o:a3f2b1c8...
```

---

## A practical mental model

Caching works well when the request is:

- repeated
- deterministic
- not dependent on external tool state
- not multimodal-heavy

Caching is usually a bad fit when the response depends on:

- tool execution
- changing world state
- dynamic media inputs
- multi-step agent loops

---

## Bring your own cache store

Spectra uses `ICacheStore` as the cache abstraction.

```csharp
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
```

Spectra includes an in-memory cache store for development.

For production, you can implement `ICacheStore` with:

- Redis
- SQLite
- a distributed cache
- any custom backend

---

## What's next?

<div class="grid cards" markdown>

- **Retry & Timeout**

  Retry transient provider failures before giving up.

  [:octicons-arrow-right-24: Retry](retry.md)

- **Provider Fallback**

  Route requests across multiple providers or models.

  [:octicons-arrow-right-24: Fallback](fallback.md)

</div>