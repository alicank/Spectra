using System.Collections.Concurrent;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Default implementation of <see cref="IToolResiliencePolicy"/> that manages
/// independent circuit breaker state for each tool. Thread-safe for concurrent
/// tool execution within agentic loops.
/// </summary>
public sealed class DefaultToolResiliencePolicy : IToolResiliencePolicy
{
    private readonly ToolResilienceOptions _options;
    private readonly ConcurrentDictionary<string, ToolCircuitRecord> _circuits = new(StringComparer.OrdinalIgnoreCase);

    public DefaultToolResiliencePolicy(ToolResilienceOptions? options = null)
    {
        _options = options ?? new ToolResilienceOptions();
    }

    public (ToolCircuitState State, bool Allowed) CanExecute(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        var record = _circuits.GetOrAdd(toolName, _ => new ToolCircuitRecord());

        lock (record.Lock)
        {
            switch (record.State)
            {
                case ToolCircuitState.Closed:
                    return (ToolCircuitState.Closed, true);

                case ToolCircuitState.Open:
                    // Check if cooldown has elapsed → transition to half-open
                    if (record.CircuitOpenedAt.HasValue &&
                        DateTimeOffset.UtcNow - record.CircuitOpenedAt.Value >= _options.CooldownPeriod)
                    {
                        var previousState = record.State;
                        record.State = ToolCircuitState.HalfOpen;
                        record.HalfOpenAttempts = 0;
                        record.ConsecutiveSuccesses = 0;
                        record.LastTransition = new StateTransition(previousState, ToolCircuitState.HalfOpen);
                        return (ToolCircuitState.HalfOpen, true);
                    }
                    return (ToolCircuitState.Open, false);

                case ToolCircuitState.HalfOpen:
                    // Allow up to HalfOpenMaxAttempts probe calls
                    if (record.HalfOpenAttempts < _options.HalfOpenMaxAttempts)
                        return (ToolCircuitState.HalfOpen, true);
                    return (ToolCircuitState.HalfOpen, false);

                default:
                    return (ToolCircuitState.Closed, true);
            }
        }
    }

    public void RecordSuccess(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        var record = _circuits.GetOrAdd(toolName, _ => new ToolCircuitRecord());

        lock (record.Lock)
        {
            record.ConsecutiveFailures = 0;

            switch (record.State)
            {
                case ToolCircuitState.Closed:
                    // Already healthy, nothing to do
                    break;

                case ToolCircuitState.HalfOpen:
                    record.HalfOpenAttempts++;
                    record.ConsecutiveSuccesses++;

                    if (record.ConsecutiveSuccesses >= _options.SuccessThresholdToClose)
                    {
                        // Recovered — close the circuit
                        record.State = ToolCircuitState.Closed;
                        record.CircuitOpenedAt = null;
                        record.HalfOpenAttempts = 0;
                        record.ConsecutiveSuccesses = 0;
                        record.LastTransition = new StateTransition(ToolCircuitState.HalfOpen, ToolCircuitState.Closed);
                    }
                    break;
            }
        }
    }

    public void RecordFailure(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        var record = _circuits.GetOrAdd(toolName, _ => new ToolCircuitRecord());

        lock (record.Lock)
        {
            record.ConsecutiveFailures++;
            record.LastFailureTime = DateTimeOffset.UtcNow;

            switch (record.State)
            {
                case ToolCircuitState.Closed:
                    if (record.ConsecutiveFailures >= _options.FailureThreshold)
                    {
                        record.State = ToolCircuitState.Open;
                        record.CircuitOpenedAt = DateTimeOffset.UtcNow;
                        record.LastTransition = new StateTransition(ToolCircuitState.Closed, ToolCircuitState.Open);
                    }
                    break;

                case ToolCircuitState.HalfOpen:
                    // Probe failed — reopen immediately
                    record.HalfOpenAttempts++;
                    record.State = ToolCircuitState.Open;
                    record.CircuitOpenedAt = DateTimeOffset.UtcNow;
                    record.ConsecutiveSuccesses = 0;
                    record.LastTransition = new StateTransition(ToolCircuitState.HalfOpen, ToolCircuitState.Open);
                    break;
            }
        }
    }

    public ToolCircuitInfo GetInfo(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        var record = _circuits.GetOrAdd(toolName, _ => new ToolCircuitRecord());

        lock (record.Lock)
        {
            return new ToolCircuitInfo
            {
                ToolName = toolName,
                State = record.State,
                ConsecutiveFailures = record.ConsecutiveFailures,
                ConsecutiveSuccesses = record.ConsecutiveSuccesses,
                LastFailureTime = record.LastFailureTime,
                CircuitOpenedAt = record.CircuitOpenedAt
            };
        }
    }

    public string? GetFallbackToolName(string toolName)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        return _options.FallbackTools.TryGetValue(toolName, out var fallback) ? fallback : null;
    }

    /// <summary>
    /// Returns the last state transition for a tool, or null if no transition has occurred.
    /// Used internally by <see cref="ResilientToolDecorator"/> to detect and emit transition events.
    /// </summary>
    internal StateTransition? GetLastTransition(string toolName)
    {
        if (!_circuits.TryGetValue(toolName, out var record))
            return null;

        lock (record.Lock)
        {
            var transition = record.LastTransition;
            record.LastTransition = null; // consume once
            return transition;
        }
    }

    private sealed class ToolCircuitRecord
    {
        public readonly object Lock = new();
        public ToolCircuitState State = ToolCircuitState.Closed;
        public int ConsecutiveFailures;
        public int ConsecutiveSuccesses;
        public int HalfOpenAttempts;
        public DateTimeOffset? LastFailureTime;
        public DateTimeOffset? CircuitOpenedAt;
        public StateTransition? LastTransition;
    }

    internal record StateTransition(ToolCircuitState From, ToolCircuitState To);
}