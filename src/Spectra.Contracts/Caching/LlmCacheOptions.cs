namespace Spectra.Contracts.Caching;

/// <summary>
/// Configuration for the LLM response caching decorator.
/// </summary>
public class LlmCacheOptions
{
    /// <summary>
    /// Default time-to-live for cached responses. Null means entries never expire.
    /// Default time-to-live for cached responses. Null means entries never expire.
    /// </summary>
    public TimeSpan? DefaultTtl { get; init; }

    /// <summary>
    /// Master switch for the caching decorator. When false, the decorator becomes a
    /// transparent pass-through without requiring pipeline reconstruction.
    /// Defaults to true so caching is active unless explicitly disabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Prefix prepended to all cache keys. Useful for multi-tenant or per-environment isolation.
    /// </summary>
    public string KeyPrefix { get; init; } = "spectra:llm:";

    /// <summary>
    /// When true, responses containing tool calls are not cached
    /// because tool call results depend on external state.
    /// </summary>
    public bool SkipWhenToolCalls { get; init; } = true;

    /// <summary>
    /// When true, requests containing media content (images, audio, video) bypass the cache
    /// to avoid storing large binary data.
    /// </summary>
    public bool SkipWhenMedia { get; init; } = true;
}