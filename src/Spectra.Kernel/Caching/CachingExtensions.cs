using Spectra.Contracts.Caching;
using Spectra.Contracts.Providers;

namespace Spectra.Kernel.Caching;

/// <summary>
/// Extension methods for applying the caching decorator to LLM clients.
/// </summary>
public static class CachingExtensions
{
    /// <summary>
    /// Wraps an <see cref="ILlmClient"/> with response caching.
    /// </summary>
    public static ILlmClient WithCaching(
        this ILlmClient client,
        ICacheStore cache,
        LlmCacheOptions? options = null)
    {
        return new CachingLlmClient(client, cache, options);
    }
}