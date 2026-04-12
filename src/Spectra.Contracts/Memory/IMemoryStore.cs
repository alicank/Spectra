namespace Spectra.Contracts.Memory;

/// <summary>
/// Persistent key-value store scoped by namespace for cross-session memory.
/// Implement this contract to back memory with Redis, Postgres, Cosmos DB, etc.
/// </summary>
public interface IMemoryStore
{
    /// <summary>Retrieves a single entry by namespace and key. Returns null if not found or expired.</summary>
    Task<MemoryEntry?> GetAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an entry. If the key already exists, it is overwritten.</summary>
    Task SetAsync(
        string @namespace,
        string key,
        MemoryEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a single entry. No-op if the entry does not exist.</summary>
    Task DeleteAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all non-expired entries in a namespace, ordered by <see cref="MemoryEntry.UpdatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for entries matching the query. Stores that do not support search
    /// (see <see cref="Capabilities"/>) return an empty list.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Removes ALL entries in a namespace.</summary>
    Task PurgeAsync(
        string @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>Declares what this store implementation supports.</summary>
    MemoryStoreCapabilities Capabilities { get; }
}