namespace Spectra.Contracts.Memory;

/// <summary>
/// Declares what a memory store implementation supports.
/// Consumers can inspect this to adapt behavior gracefully
/// rather than catching <see cref="NotSupportedException"/>.
/// </summary>
public sealed record MemoryStoreCapabilities
{
    /// <summary>Whether the store supports text or semantic search via <see cref="IMemoryStore.SearchAsync"/>.</summary>
    public bool CanSearch { get; init; }

    /// <summary>Whether the store honors <see cref="MemoryEntry.ExpiresAt"/> and automatically evicts expired entries.</summary>
    public bool CanExpire { get; init; }

    /// <summary>Whether the store supports filtering by <see cref="MemoryEntry.Tags"/>.</summary>
    public bool CanFilterByTags { get; init; }

    /// <summary>Whether the store supports filtering by <see cref="MemoryEntry.Metadata"/>.</summary>
    public bool CanFilterByMetadata { get; init; }

    /// <summary>Maximum size in bytes for a single entry's content. Null means unlimited.</summary>
    public long? MaxEntrySize { get; init; }

    /// <summary>Capabilities for the built-in in-memory store.</summary>
    public static MemoryStoreCapabilities InMemory => new()
    {
        CanSearch = true,
        CanExpire = true,
        CanFilterByTags = true,
        CanFilterByMetadata = true
    };
}