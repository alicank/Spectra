namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Defines how multiple providers are selected when executing an LLM request.
/// </summary>
public enum FallbackStrategy
{
    /// <summary>
    /// Sequential cascade: try providers in order, move to the next only on failure.
    /// Classic failover pattern — "OpenAI is down, try Anthropic."
    /// </summary>
    Failover,

    /// <summary>
    /// Rotate through providers equally on each request (thread-safe counter).
    /// Distributes load evenly and avoids hitting a single provider's rate limits.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Probabilistic selection based on configured weights.
    /// Example: 70% OpenAI / 30% Anthropic. On failure, falls back to next by weight.
    /// </summary>
    Weighted,

    /// <summary>
    /// Deterministic percentage-based split using a counter.
    /// Similar to AWS weighted routing — requests are bucketed by percentage.
    /// On failure of the selected provider, cascades through remaining providers.
    /// </summary>
    Split
}