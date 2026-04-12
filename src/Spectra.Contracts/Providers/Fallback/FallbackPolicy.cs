namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Concrete implementation of <see cref="IFallbackPolicy"/>.
/// </summary>
public class FallbackPolicy : IFallbackPolicy
{
    public required string Name { get; init; }
    public FallbackStrategy Strategy { get; init; } = FallbackStrategy.Failover;
    public IReadOnlyList<FallbackProviderEntry> Entries { get; init; } = [];
    public IQualityGate? DefaultQualityGate { get; init; }
}