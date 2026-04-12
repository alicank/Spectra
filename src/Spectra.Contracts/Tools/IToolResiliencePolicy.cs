namespace Spectra.Contracts.Tools;

/// <summary>
/// Manages per-tool circuit breaker state. Each tool gets an independent circuit
/// that transitions through closed → open → half-open based on failure patterns.
/// Implementations must be thread-safe for concurrent tool execution.
/// </summary>
public interface IToolResiliencePolicy
{
    /// <summary>
    /// Checks whether a tool call is currently allowed by the circuit breaker.
    /// Returns the current circuit state and whether execution should proceed.
    /// </summary>
    /// <param name="toolName">The registered name of the tool.</param>
    /// <returns>A tuple of (state, allowed). When <c>allowed</c> is false,
    /// the caller should skip execution or use a fallback.</returns>
    (ToolCircuitState State, bool Allowed) CanExecute(string toolName);

    /// <summary>
    /// Records a successful tool execution. In half-open state, may close the circuit.
    /// </summary>
    void RecordSuccess(string toolName);

    /// <summary>
    /// Records a failed tool execution. In closed state, may open the circuit.
    /// In half-open state, reopens the circuit immediately.
    /// </summary>
    void RecordFailure(string toolName);

    /// <summary>
    /// Returns diagnostic information about a tool's circuit breaker state.
    /// </summary>
    ToolCircuitInfo GetInfo(string toolName);

    /// <summary>
    /// Returns the configured fallback tool name for the given tool, if any.
    /// </summary>
    string? GetFallbackToolName(string toolName);
}

/// <summary>
/// Diagnostic snapshot of a tool's circuit breaker state.
/// </summary>
public record ToolCircuitInfo
{
    public required string ToolName { get; init; }
    public required ToolCircuitState State { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int ConsecutiveSuccesses { get; init; }
    public DateTimeOffset? LastFailureTime { get; init; }
    public DateTimeOffset? CircuitOpenedAt { get; init; }
}