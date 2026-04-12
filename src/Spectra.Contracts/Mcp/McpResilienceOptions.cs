namespace Spectra.Contracts.Mcp;

/// <summary>
/// Resilience configuration for MCP server connections.
/// Mirrors <see cref="Providers.LlmResilienceOptions"/> for consistency.
/// </summary>
public record McpResilienceOptions
{
    /// <summary>
    /// Maximum number of retry attempts after the initial call fails.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Base delay between retries.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the delay between retries.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Per-call timeout for individual MCP tool invocations.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, delay doubles on each retry with random jitter.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// For stdio transport: whether to restart the process on crash.
    /// </summary>
    public bool RestartOnCrash { get; init; } = true;

    /// <summary>
    /// Maximum number of consecutive failures before the server is marked unhealthy.
    /// Subsequent calls fail fast until the cooldown expires. 0 = disabled.
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>
    /// How long to wait before retrying a server that was marked unhealthy.
    /// </summary>
    public TimeSpan CircuitBreakerCooldown { get; init; } = TimeSpan.FromSeconds(60);
}