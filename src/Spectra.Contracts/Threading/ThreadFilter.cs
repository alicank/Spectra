namespace Spectra.Contracts.Threading;

/// <summary>
/// Filter criteria for querying threads.
/// All properties are optional and combined with AND semantics.
/// </summary>
public sealed class ThreadFilter
{
    /// <summary>Filter by tenant.</summary>
    public string? TenantId { get; set; }

    /// <summary>Filter by user.</summary>
    public string? UserId { get; set; }

    /// <summary>Filter by workflow definition.</summary>
    public string? WorkflowId { get; set; }

    /// <summary>Only return threads created before this time.</summary>
    public DateTimeOffset? CreatedBefore { get; set; }

    /// <summary>Only return threads created after this time.</summary>
    public DateTimeOffset? CreatedAfter { get; set; }

    /// <summary>Only return threads that contain ALL of these tags.</summary>
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>Optional label substring match.</summary>
    public string? LabelContains { get; set; }
}