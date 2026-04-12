namespace Spectra.Contracts.Memory;

/// <summary>
/// A single result from a memory search, pairing the entry with a relevance score.
/// </summary>
public sealed record MemorySearchResult
{
    public required MemoryEntry Entry { get; init; }

    /// <summary>
    /// Relevance score between 0.0 and 1.0. Higher is more relevant.
    /// For exact-match stores this is always 1.0.
    /// </summary>
    public double Score { get; init; } = 1.0;
}