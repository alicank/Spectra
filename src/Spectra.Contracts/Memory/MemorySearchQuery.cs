namespace Spectra.Contracts.Memory;

/// <summary>
/// Describes a search request against the memory store.
/// Stores that do not support search return an empty result set.
/// </summary>
public sealed class MemorySearchQuery
{
    /// <summary>
    /// Required scope for the search.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Free-text or semantic query. Interpretation depends on the store
    /// (substring match, full-text, vector similarity, etc.).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Filter results to entries that contain ALL specified tags.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Filter results to entries whose metadata contains ALL specified key-value pairs (exact match).
    /// </summary>
    public Dictionary<string, string>? MetadataFilters { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int MaxResults { get; init; } = 10;

    /// <summary>
    /// When true, expired entries are included in results.
    /// </summary>
    public bool IncludeExpired { get; init; }
}