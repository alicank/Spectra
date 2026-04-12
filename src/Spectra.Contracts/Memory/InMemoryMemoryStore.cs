using System.Collections.Concurrent;

namespace Spectra.Contracts.Memory;

/// <summary>
/// In-memory implementation of <see cref="IMemoryStore"/> for development and testing.
/// Data is lost when the process exits. For production, implement the interface
/// with a durable backend (Redis, Postgres, Cosmos DB, etc.).
/// </summary>
public sealed class InMemoryMemoryStore : IMemoryStore
{
    // namespace → (key → entry)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MemoryEntry>> _store = new();
    private readonly object _lock = new();

    public MemoryStoreCapabilities Capabilities => MemoryStoreCapabilities.InMemory;

    public Task<MemoryEntry?> GetAsync(
        string @namespace, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_store.TryGetValue(@namespace, out var bucket)
            || !bucket.TryGetValue(key, out var entry))
            return Task.FromResult<MemoryEntry?>(null);

        if (IsExpired(entry))
        {
            bucket.TryRemove(key, out _);
            return Task.FromResult<MemoryEntry?>(null);
        }

        return Task.FromResult<MemoryEntry?>(entry);
    }

    public Task SetAsync(
        string @namespace, string key, MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stamped = entry with
        {
            Key = key,
            Namespace = @namespace,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var bucket = _store.GetOrAdd(@namespace, _ => new ConcurrentDictionary<string, MemoryEntry>());
        bucket[key] = stamped;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string @namespace, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_store.TryGetValue(@namespace, out var bucket))
            bucket.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string @namespace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_store.TryGetValue(@namespace, out var bucket))
            return Task.FromResult<IReadOnlyList<MemoryEntry>>(Array.Empty<MemoryEntry>());

        var results = bucket.Values
            .Where(e => !IsExpired(e))
            .OrderByDescending(e => e.UpdatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(results);
    }

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_store.TryGetValue(query.Namespace, out var bucket))
            return Task.FromResult<IReadOnlyList<MemorySearchResult>>(Array.Empty<MemorySearchResult>());

        var candidates = bucket.Values.AsEnumerable();

        // Filter expired
        if (!query.IncludeExpired)
            candidates = candidates.Where(e => !IsExpired(e));

        // Filter by tags (AND logic)
        if (query.Tags is { Count: > 0 })
            candidates = candidates.Where(e =>
                query.Tags.All(t => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));

        // Filter by metadata (AND, exact match)
        if (query.MetadataFilters is { Count: > 0 })
            candidates = candidates.Where(e =>
                query.MetadataFilters.All(f =>
                    e.Metadata.TryGetValue(f.Key, out var v)
                    && string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase)));

        // Text search: simple case-insensitive substring on content + key
        var results = candidates.Select(e =>
        {
            var score = 1.0;
            if (!string.IsNullOrEmpty(query.Text))
            {
                var text = query.Text;
                var keyMatch = e.Key.Contains(text, StringComparison.OrdinalIgnoreCase);
                var contentMatch = e.Content.Contains(text, StringComparison.OrdinalIgnoreCase);

                if (!keyMatch && !contentMatch)
                    return null;

                score = keyMatch ? 1.0 : 0.8;
            }

            return new MemorySearchResult { Entry = e, Score = score };
        })
        .Where(r => r is not null)
        .OrderByDescending(r => r!.Score)
        .ThenByDescending(r => r!.Entry.UpdatedAt)
        .Take(query.MaxResults)
        .Cast<MemorySearchResult>()
        .ToList();

        return Task.FromResult<IReadOnlyList<MemorySearchResult>>(results);
    }

    public Task PurgeAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryRemove(@namespace, out _);
        return Task.CompletedTask;
    }

    private static bool IsExpired(MemoryEntry entry) =>
        entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
}