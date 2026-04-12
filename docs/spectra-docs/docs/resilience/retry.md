---
description: "Configure retry and per-attempt timeout behavior for LLM calls in Spectra."
---

# Retry & Timeout

LLM calls fail in real systems.

Common failures include:

- rate limits
- transient server errors
- network errors
- requests that take too long

Spectra handles this with `ResilientLlmClient`, which adds:

- **retries** for transient failures
- **per-attempt timeouts**
- **backoff** between attempts

Permanent failures fail fast.

---

## What it does

`ResilientLlmClient` wraps an `ILlmClient` and retries only when the failure looks temporary.

That means:

- retry on rate limits and transient server problems
- retry on timeouts and network errors
- do **not** retry bad requests or auth failures

Each retry attempt gets its own timeout window.

---

## Configuration

```csharp
var options = new LlmResilienceOptions
{
    MaxRetries = 3,
    BaseDelay = TimeSpan.FromSeconds(1),
    MaxDelay = TimeSpan.FromSeconds(30),
    Timeout = TimeSpan.FromSeconds(60),
    UseExponentialBackoff = true,
    RetryableStatusCodes = new HashSet<int>
    {
        429,
        500,
        502,
        503,
        504
    }
};
```

### Options reference

| Option | Default | Description |
| --- | --- | --- |
| `MaxRetries` | `3` | Number of retry attempts after the initial failure |
| `BaseDelay` | `1s` | Starting delay between retries |
| `MaxDelay` | `30s` | Maximum delay between retries |
| `Timeout` | `60s` | Per-attempt timeout |
| `UseExponentialBackoff` | `true` | Doubles delay on each retry, with jitter |
| `RetryableStatusCodes` | `429, 500-504` | HTTP codes treated as transient |

Set `MaxRetries = 0` to disable retries.

Set `Timeout = Timeout.InfiniteTimeSpan` to disable per-attempt timeouts.

---

## Backoff behavior

With the default settings:

- `BaseDelay = 1s`
- exponential backoff enabled

the retry timing looks roughly like this:

| Attempt | Delay before attempt |
| --- | --- |
| 1 | none |
| 2 | about `1.0 – 1.25s` |
| 3 | about `2.0 – 2.5s` |
| 4 | about `4.0 – 5.0s` |

Spectra adds jitter so many workflows do not all retry at exactly the same moment.

That helps reduce retry storms under load.

---

## What gets retried

| Failure type | Retried? |
| --- | --- |
| HTTP `429` | Yes |
| HTTP `500`, `502`, `503`, `504` | Yes |
| Per-attempt timeout | Yes |
| `HttpRequestException` | Yes |
| HTTP `400` | No |
| HTTP `401` / `403` | No |
| Other failures | Only if treated as retryable by configuration |

A simple rule:

- **temporary problem** → retry
- **bad request or auth problem** → fail fast

---

## How timeout works

The timeout applies **per attempt**, not to the full retry sequence.

So with:

- `Timeout = 60s`
- `MaxRetries = 3`

you could have up to four attempts total:

- 1 initial attempt
- 3 retries

Each one can run for up to 60 seconds before timing out.

That makes timeout behavior predictable and easier to tune.

---

## Advanced usage

Most applications use the resilient client as part of Spectra's normal provider/client composition.

If you need manual composition:

```csharp
var raw = provider.CreateClient(agent);
var resilient = new ResilientLlmClient(raw, options);
```

This is mainly useful for advanced customization or testing.

---

## A practical mental model

`ResilientLlmClient` answers one question:

**Was this failure temporary enough to try again?**

- if yes, retry with delay
- if no, stop immediately

That is the core behavior.

---

## What's next?

<div class="grid cards" markdown>

- **Provider Fallback**

  Route failures across alternative providers or models.

  [:octicons-arrow-right-24: Fallback](fallback.md)

- **Caching**

  Avoid repeated LLM calls for the same request.

  [:octicons-arrow-right-24: Caching](caching.md)

</div>