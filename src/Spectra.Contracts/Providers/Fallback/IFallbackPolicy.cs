namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Defines a named fallback policy that governs how multiple LLM providers
/// are selected and cascaded for a given workflow step or agent.
/// </summary>
public interface IFallbackPolicy
{
    /// <summary>
    /// Unique name for this policy. Referenced by agents or steps.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The routing/fallback strategy.
    /// </summary>
    FallbackStrategy Strategy { get; }

    /// <summary>
    /// Ordered list of provider entries in the cascade.
    /// </summary>
    IReadOnlyList<FallbackProviderEntry> Entries { get; }

    /// <summary>
    /// Default quality gate applied to all responses unless overridden per-entry.
    /// </summary>
    IQualityGate? DefaultQualityGate { get; }
}