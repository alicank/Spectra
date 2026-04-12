namespace Spectra.Contracts.Tools;

/// <summary>
/// Represents the state of a per-tool circuit breaker.
/// Follows the standard closed → open → half-open pattern.
/// </summary>
public enum ToolCircuitState
{
    /// <summary>
    /// Normal operation — tool calls are allowed through.
    /// Transitions to <see cref="Open"/> when consecutive failures reach the threshold.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is tripped — tool calls are rejected immediately without execution.
    /// Transitions to <see cref="HalfOpen"/> after the cooldown period expires.
    /// </summary>
    Open,

    /// <summary>
    /// Probing state — a limited number of calls are allowed through to test recovery.
    /// Transitions to <see cref="Closed"/> on success or back to <see cref="Open"/> on failure.
    /// </summary>
    HalfOpen
}