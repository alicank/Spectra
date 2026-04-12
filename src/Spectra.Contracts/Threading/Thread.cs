using System.Text.Json.Serialization;

namespace Spectra.Contracts.Threading;

/// <summary>
/// Represents a managed conversation thread — a logical grouping of workflow runs
/// and their checkpoint history. Threads are the unit of lifecycle management:
/// create, clone, query, retain, and purge.
/// </summary>
public sealed record Thread
{
    /// <summary>Unique thread identifier. Defaults to a new GUID.</summary>
    public required string ThreadId { get; init; }

    /// <summary>The workflow definition this thread is associated with.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Tenant identifier for multi-tenant filtering.</summary>
    public string? TenantId { get; init; }

    /// <summary>User identifier for per-user filtering.</summary>
    public string? UserId { get; init; }

    /// <summary>Human-readable label for the thread.</summary>
    public string? Label { get; init; }

    /// <summary>Arbitrary tags for categorisation and filtering.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The run ID that currently represents this thread's execution.</summary>
    public required string RunId { get; init; }

    /// <summary>Arbitrary metadata the consumer wants stored with the thread.</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When non-null, this thread was cloned from another thread.</summary>
    public string? SourceThreadId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? ExtensionData { get; init; }
}