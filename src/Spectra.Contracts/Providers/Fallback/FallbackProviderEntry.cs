namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Defines a single provider+model in a fallback chain.
/// </summary>
public class FallbackProviderEntry
{
    /// <summary>
    /// The provider name (e.g., "openai", "anthropic", "ollama").
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The model ID to use with this provider.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Weight for <see cref="FallbackStrategy.Weighted"/> and
    /// <see cref="FallbackStrategy.Split"/> strategies.
    /// Values are relative — e.g., 70 and 30 yield a 70/30 split.
    /// Ignored for <see cref="FallbackStrategy.Failover"/> and
    /// <see cref="FallbackStrategy.RoundRobin"/>.
    /// </summary>
    public int Weight { get; init; } = 1;

    /// <summary>
    /// Optional per-entry quality gate. When set, overrides the policy-level gate
    /// for responses from this specific provider.
    /// </summary>
    public IQualityGate? QualityGate { get; init; }

    /// <summary>
    /// Optional maximum requests per minute budget for this entry.
    /// When the budget is exceeded, the entry is skipped proactively.
    /// </summary>
    public int? MaxRequestsPerMinute { get; init; }
}