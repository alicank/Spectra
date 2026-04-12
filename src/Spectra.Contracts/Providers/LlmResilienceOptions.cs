namespace Spectra.Contracts.Providers;

/// <summary>
/// Configuration for the resilient LLM client decorator.
/// Controls timeout, retry count, backoff strategy, and which failures are retryable.
/// </summary>
public record LlmResilienceOptions
{
    /// <summary>
    /// Maximum number of retry attempts after the initial call fails.
    /// Set to 0 to disable retries (timeout still applies).
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Base delay between retries. Doubled on each attempt when
    /// <see cref="UseExponentialBackoff"/> is true.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the delay between retries.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Per-call timeout applied to each individual attempt (not cumulative).
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// When true, delay doubles on each retry with random jitter.
    /// When false, <see cref="BaseDelay"/> is used as a fixed interval.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// HTTP status codes that are considered transient and eligible for retry.
    /// Parsed from <see cref="LlmResponse.ErrorMessage"/> when the response
    /// indicates failure.
    /// </summary>
    public HashSet<int> RetryableStatusCodes { get; init; } = new()
    {
        429, // Too Many Requests
        500, // Internal Server Error
        502, // Bad Gateway
        503, // Service Unavailable
        504  // Gateway Timeout
    };
}