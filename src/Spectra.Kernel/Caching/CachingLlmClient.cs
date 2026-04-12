using Spectra.Contracts.Caching;
using Spectra.Contracts.Providers;

namespace Spectra.Kernel.Caching;

/// <summary>
/// Decorator that caches LLM responses. Wraps any <see cref="ILlmClient"/>
/// and uses an <see cref="ICacheStore"/> for storage.
/// </summary>
public sealed class CachingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly ICacheStore _cache;
    private readonly LlmCacheOptions _options;

    public string ProviderName => _inner.ProviderName;
    public string ModelId => _inner.ModelId;
    public ModelCapabilities Capabilities => _inner.Capabilities;

    public CachingLlmClient(ILlmClient inner, ICacheStore cache, LlmCacheOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? new LlmCacheOptions();
    }

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ShouldSkipCache(request))
            return await _inner.CompleteAsync(request, cancellationToken);

        var key = CacheKeyGenerator.Generate(_options.KeyPrefix, request);

        var cached = await _cache.GetAsync<LlmResponse>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var response = await _inner.CompleteAsync(request, cancellationToken);

        if (response.Success && ShouldCacheResponse(response))
            await _cache.SetAsync(key, response, _options.DefaultTtl, cancellationToken);

        return response;
    }

    private bool ShouldSkipCache(LlmRequest request)
    {
        if (!_options.Enabled)
            return true;

        if (request.SkipCache)
            return true;

        if (_options.SkipWhenMedia && request.Messages.Any(m => m.HasMedia))
            return true;

        return false;
    }


    private bool ShouldCacheResponse(LlmResponse response)
    {
        if (_options.SkipWhenToolCalls && response.HasToolCalls)
            return false;

        return true;
    }
}