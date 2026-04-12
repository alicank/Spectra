using System.Text.Json;

namespace Spectra.Contracts.Memory;

/// <summary>
/// A single unit of persistent memory — a named piece of knowledge
/// that outlives a single workflow run.
/// Content is stored as an opaque JSON string; use the typed extension
/// methods (<see cref="MemoryEntryExtensions.GetValue{T}"/>) for convenience.
/// </summary>
public sealed record MemoryEntry
{
    public required string Key { get; init; }

    public required string Namespace { get; init; }

    /// <summary>
    /// The stored data, typically serialized JSON.
    /// </summary>
    public required string Content { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Flat, indexable metadata. Useful for filtering in store implementations
    /// that support metadata queries (SQL WHERE, Redis HASH fields, etc.).
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional expiration. Null means the entry never expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public int SchemaVersion { get; init; } = 1;
}

/// <summary>
/// Typed convenience methods for <see cref="MemoryEntry"/>.
/// </summary>
public static class MemoryEntryExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserializes the <see cref="MemoryEntry.Content"/> into <typeparamref name="T"/>.
    /// Returns <c>null</c> if deserialization fails.
    /// </summary>
    public static T? GetValue<T>(this MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(entry.Content, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Creates a new <see cref="MemoryEntry"/> with the value serialized as JSON content.
    /// </summary>
    public static MemoryEntry Create<T>(
        string @namespace,
        string key,
        T value,
        IReadOnlyList<string>? tags = null,
        Dictionary<string, string>? metadata = null,
        TimeSpan? ttl = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return new MemoryEntry
        {
            Key = key,
            Namespace = @namespace,
            Content = json,
            Tags = tags ?? [],
            Metadata = metadata ?? [],
            ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : null
        };
    }

    /// <summary>
    /// Returns a copy of the entry with new content serialized from <paramref name="value"/>.
    /// </summary>
    public static MemoryEntry WithValue<T>(this MemoryEntry entry, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return entry with
        {
            Content = json,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}