namespace Spectra.Contracts.Events;

/// <summary>
/// Emitted when a provider fails and the fallback chain moves to the next provider.
/// </summary>
public sealed record FallbackTriggeredEvent : WorkflowEvent
{
    /// <summary>Provider that failed.</summary>
    public required string FailedProvider { get; init; }

    /// <summary>Model that failed.</summary>
    public required string FailedModel { get; init; }

    /// <summary>Error from the failed provider.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Provider being tried next.</summary>
    public required string NextProvider { get; init; }

    /// <summary>Model being tried next.</summary>
    public required string NextModel { get; init; }

    /// <summary>1-based attempt index within the fallback chain.</summary>
    public int AttemptIndex { get; init; }

    /// <summary>The fallback strategy in use.</summary>
    public required string Strategy { get; init; }

    /// <summary>Name of the fallback policy.</summary>
    public required string PolicyName { get; init; }
}

/// <summary>
/// Emitted when all providers in the fallback chain have been exhausted.
/// </summary>
public sealed record FallbackExhaustedEvent : WorkflowEvent
{
    /// <summary>Total number of providers attempted.</summary>
    public int TotalAttempts { get; init; }

    /// <summary>Error from the last provider.</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>Name of the fallback policy.</summary>
    public required string PolicyName { get; init; }

    /// <summary>The fallback strategy that was in use.</summary>
    public required string Strategy { get; init; }
}

/// <summary>
/// Emitted when a quality gate rejects a fallback response.
/// </summary>
public sealed record QualityGateRejectedEvent : WorkflowEvent
{
    /// <summary>Provider whose response was rejected.</summary>
    public required string Provider { get; init; }

    /// <summary>Model whose response was rejected.</summary>
    public required string Model { get; init; }

    /// <summary>Reason for rejection from the quality gate.</summary>
    public required string Reason { get; init; }

    /// <summary>Name of the fallback policy.</summary>
    public required string PolicyName { get; init; }
}