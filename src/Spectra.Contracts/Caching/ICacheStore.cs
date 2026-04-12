namespace Spectra.Contracts.Caching;

/// <summary>
/// Abstraction for key-value caching. Bring your own implementation
/// (Redis, SQLite, distributed cache, etc.) or use the built-in in-memory store.
/// </summary>
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}