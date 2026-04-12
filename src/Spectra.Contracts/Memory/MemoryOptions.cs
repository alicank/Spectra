namespace Spectra.Contracts.Memory;

/// <summary>
/// Configuration for long-term memory behavior at the workflow or runner level.
/// </summary>
public sealed class MemoryOptions
{
    /// <summary>
    /// Default namespace used when a step or tool omits one.
    /// </summary>
    public string DefaultNamespace { get; set; } = MemoryNamespace.Global;

    /// <summary>
    /// Default time-to-live for new memory entries. Null means entries never expire.
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>
    /// Maximum number of entries allowed per namespace.
    /// When exceeded, the oldest entries are evicted on write.
    /// Null means unlimited.
    /// </summary>
    public int? MaxEntriesPerNamespace { get; set; }

    /// <summary>
    /// When true, <c>recall_memory</c> and <c>store_memory</c> tools are
    /// automatically injected into agent nodes that have memory configured.
    /// </summary>
    public bool AutoInjectAgentTools { get; set; }

    public static MemoryOptions Default => new();
}