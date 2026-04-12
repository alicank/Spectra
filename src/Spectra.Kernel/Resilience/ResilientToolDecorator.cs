using System.Diagnostics;
using Spectra.Contracts.Diagnostics;
using Spectra.Contracts.Events;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Diagnostics;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Decorator that wraps any <see cref="ITool"/> with per-tool circuit breaker protection.
/// When a tool's circuit opens (too many consecutive failures), calls are either
/// routed to a fallback tool or rejected with a descriptive error — preventing
/// cascading failures in agentic loops.
///
/// Integrates with:
/// <list type="bullet">
///   <item><see cref="IEventSink"/> — emits <see cref="ToolCircuitStateChangedEvent"/>
///   and <see cref="ToolCallSkippedEvent"/> for observability and audit trail.</item>
///   <item><see cref="SpectraActivitySource"/> — creates OTel spans with circuit state tags.</item>
/// </list>
/// </summary>
internal sealed class ResilientToolDecorator : ITool
{
    private readonly ITool _inner;
    private readonly DefaultToolResiliencePolicy _policy;
    private readonly IToolRegistry _toolRegistry;
    private readonly IEventSink? _eventSink;

    public string Name => _inner.Name;
    public ToolDefinition Definition => _inner.Definition;

    public ResilientToolDecorator(
        ITool inner,
        DefaultToolResiliencePolicy policy,
        IToolRegistry toolRegistry,
        IEventSink? eventSink = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _eventSink = eventSink;
    }

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        var toolName = _inner.Name;

        // Start OTel span for this tool execution
        using var activity = SpectraActivitySource.StartToolExecution(toolName);

        // Check circuit state
        var (circuitState, allowed) = _policy.CanExecute(toolName);
        activity?.SetTag(SpectraTags.ToolCircuitState, circuitState.ToString());

        if (!allowed)
        {
            // Circuit is open — try fallback or report degraded
            var fallbackName = _policy.GetFallbackToolName(toolName);
            var fallbackTool = fallbackName is not null ? _toolRegistry.Get(fallbackName) : null;

            activity?.SetTag(SpectraTags.ToolFallbackUsed, fallbackTool is not null);
            if (fallbackName is not null)
                activity?.SetTag(SpectraTags.ToolFallbackName, fallbackName);

            await EmitSkippedEventAsync(state, toolName, circuitState, fallbackName, fallbackTool is not null, ct);

            if (fallbackTool is not null)
            {
                // Execute fallback tool instead
                return await fallbackTool.ExecuteAsync(arguments, state, ct);
            }

            // No fallback — fail fast with descriptive error
            return ToolResult.Fail(
                $"Tool '{toolName}' is unavailable (circuit breaker is {circuitState}). " +
                "The tool has experienced repeated failures and is temporarily disabled. " +
                "Try again later or use an alternative approach.");
        }

        // Execute the inner tool
        try
        {
            var result = await _inner.ExecuteAsync(arguments, state, ct);

            if (result.Success)
            {
                _policy.RecordSuccess(toolName);
            }
            else
            {
                _policy.RecordFailure(toolName);
                activity?.SetTag(SpectraTags.ToolCircuitFailureCount,
                    _policy.GetInfo(toolName).ConsecutiveFailures);
            }

            // Check and emit state transition events
            await EmitTransitionEventIfNeededAsync(state, toolName, ct);

            return result;
        }
        catch (OperationCanceledException)
        {
            // Don't count cancellation as a tool failure
            throw;
        }
        catch (Exception ex)
        {
            _policy.RecordFailure(toolName);
            activity?.SetTag(SpectraTags.ToolCircuitFailureCount,
                _policy.GetInfo(toolName).ConsecutiveFailures);

            await EmitTransitionEventIfNeededAsync(state, toolName, ct);

            SpectraActivitySource.RecordError(activity, ex);
            throw;
        }
    }

    private async Task EmitTransitionEventIfNeededAsync(
        WorkflowState state, string toolName, CancellationToken ct)
    {
        if (_eventSink is null) return;

        var transition = _policy.GetLastTransition(toolName);
        if (transition is null) return;

        var info = _policy.GetInfo(toolName);

        var reason = transition switch
        {
            { From: ToolCircuitState.Closed, To: ToolCircuitState.Open }
                => $"Consecutive failures reached threshold ({info.ConsecutiveFailures})",
            { From: ToolCircuitState.HalfOpen, To: ToolCircuitState.Open }
                => "Probe call failed during half-open state",
            { From: ToolCircuitState.HalfOpen, To: ToolCircuitState.Closed }
                => "Probe call succeeded — tool recovered",
            { From: ToolCircuitState.Open, To: ToolCircuitState.HalfOpen }
                => "Cooldown elapsed — probing recovery",
            _ => $"State transition: {transition.From} → {transition.To}"
        };

        await _eventSink.PublishAsync(new ToolCircuitStateChangedEvent
        {
            RunId = state.RunId,
            WorkflowId = state.WorkflowId,
            EventType = "tool.circuit_state_changed",
            ToolName = toolName,
            PreviousState = transition.From.ToString(),
            NewState = transition.To.ToString(),
            ConsecutiveFailures = info.ConsecutiveFailures,
            Reason = reason
        }, ct);
    }

    private async Task EmitSkippedEventAsync(
        WorkflowState state,
        string toolName,
        ToolCircuitState circuitState,
        string? fallbackToolName,
        bool fallbackUsed,
        CancellationToken ct)
    {
        if (_eventSink is null) return;

        await _eventSink.PublishAsync(new ToolCallSkippedEvent
        {
            RunId = state.RunId,
            WorkflowId = state.WorkflowId,
            EventType = "tool.call_skipped",
            ToolName = toolName,
            CircuitState = circuitState.ToString(),
            FallbackToolName = fallbackToolName,
            FallbackUsed = fallbackUsed,
            Reason = fallbackUsed
                ? $"Circuit open for '{toolName}'; routed to fallback '{fallbackToolName}'"
                : $"Circuit open for '{toolName}'; no fallback configured — reporting degraded capability"
        }, ct);
    }
}