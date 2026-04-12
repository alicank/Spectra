using System.Collections.Concurrent;
using System.Text.Json;
using Spectra.Contracts.Caching;

namespace Spectra.Kernel.Caching;

/// <summary>
/// Simple in-memory cache store for development and testing.
/// For production, implement <see cref="ICacheStore"/> with Redis, SQLite, etc.
/// </summary>
public sealed class InMemoryCacheStore : ICacheStore, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly Timer? _cleanupTimer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="cleanupInterval">
    /// How often to sweep expired entries. Null disables periodic cleanup.
    /// </param>
    public InMemoryCacheStore(TimeSpan? cleanupInterval = null)
    {
        if (cleanupInterval.HasValue)
        {
            _cleanupTimer = new Timer(
                _ => EvictExpired(),
                null,
                cleanupInterval.Value,
                cleanupInterval.Value);
        }
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!_entries.TryGetValue(key, out var entry))
            return Task.FromResult<T?>(null);

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<T?>(null);
        }

        var value = JsonSerializer.Deserialize<T>(entry.Json, JsonOptions);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var expiresAt = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : (DateTimeOffset?)null;

        _entries[key] = new CacheEntry(json, expiresAt);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private void EvictExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value <= now)
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private sealed record CacheEntry(string Json, DateTimeOffset? ExpiresAt);
}