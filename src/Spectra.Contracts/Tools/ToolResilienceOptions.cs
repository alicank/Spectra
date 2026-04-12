namespace Spectra.Contracts.Tools;

/// <summary>
/// Configuration for per-tool circuit breaker resilience.
/// Applied by <see cref="IToolResiliencePolicy"/> to protect workflows
/// from cascading failures when individual tools (MCP servers, APIs) become unhealthy.
/// </summary>
public record ToolResilienceOptions
{
    /// <summary>
    /// Number of consecutive failures before the circuit opens for a specific tool.
    /// </summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>
    /// How long a tool's circuit stays open before transitioning to half-open.
    /// </summary>
    public TimeSpan CooldownPeriod { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum number of probe calls allowed in the half-open state.
    /// If all succeed, the circuit closes. If any fail, it reopens.
    /// </summary>
    public int HalfOpenMaxAttempts { get; init; } = 1;

    /// <summary>
    /// Number of consecutive successes in half-open required to close the circuit.
    /// Must be less than or equal to <see cref="HalfOpenMaxAttempts"/>.
    /// </summary>
    public int SuccessThresholdToClose { get; init; } = 1;

    /// <summary>
    /// Optional mapping of tool names to fallback tool names.
    /// When a tool's circuit is open, the fallback tool (if registered) is used instead.
    /// Keys and values are tool names as registered in the <see cref="IToolRegistry"/>.
    /// </summary>
    public Dictionary<string, string> FallbackTools { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}