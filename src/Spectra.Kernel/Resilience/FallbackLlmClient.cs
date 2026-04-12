using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectra.Contracts.Events;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Decorator that routes LLM requests across multiple providers according to
/// a <see cref="IFallbackPolicy"/>. Supports four strategies:
/// <list type="bullet">
///   <item><see cref="FallbackStrategy.Failover"/> — sequential cascade on failure.</item>
///   <item><see cref="FallbackStrategy.RoundRobin"/> — even rotation across providers.</item>
///   <item><see cref="FallbackStrategy.Weighted"/> — probabilistic selection by weight.</item>
///   <item><see cref="FallbackStrategy.Split"/> — deterministic percentage-based bucketing.</item>
/// </list>
/// Each provider in the chain can have its own quality gate; on gate rejection
/// the response is discarded and the next provider is tried.
/// Graceful degradation events are emitted to the event sink.
/// </summary>
public sealed class FallbackLlmClient : ILlmClient
{
    private readonly IReadOnlyList<FallbackClientEntry> _entries;
    private readonly IFallbackPolicy _policy;
    private readonly IEventSink? _eventSink;
    private readonly string _runId;
    private readonly string _workflowId;
    private readonly string? _nodeId;

    private long _roundRobinCounter;
    private long _splitCounter;

    public string ProviderName => _entries[0].Client.ProviderName;
    public string ModelId => _entries[0].Client.ModelId;
    public ModelCapabilities Capabilities => _entries[0].Client.Capabilities;

    public FallbackLlmClient(
        IFallbackPolicy policy,
        IReadOnlyList<FallbackClientEntry> entries,
        IEventSink? eventSink = null,
        string runId = "",
        string workflowId = "",
        string? nodeId = null)
    {
        if (entries.Count == 0)
            throw new ArgumentException("At least one fallback entry is required.", nameof(entries));

        _policy = policy;
        _entries = entries;
        _eventSink = eventSink;
        _runId = runId;
        _workflowId = workflowId;
        _nodeId = nodeId;
    }

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderedEntries = GetOrderedEntries();
        LlmResponse? lastResponse = null;
        string? lastError = null;

        for (var i = 0; i < orderedEntries.Count; i++)
        {
            var entry = orderedEntries[i];
            cancellationToken.ThrowIfCancellationRequested();

            // Build a request with the entry's model
            var entryRequest = CloneRequestWithModel(request, entry.Entry.Model);

            try
            {
                lastResponse = await entry.Client.CompleteAsync(entryRequest, cancellationToken);

                if (!lastResponse.Success)
                {
                    lastError = lastResponse.ErrorMessage;
                    await EmitFallbackTriggered(entry, lastError, orderedEntries, i, cancellationToken);
                    continue;
                }

                // Apply quality gate
                var gate = entry.Entry.QualityGate ?? _policy.DefaultQualityGate;
                if (gate is not null)
                {
                    var gateResult = gate.Evaluate(lastResponse);
                    if (!gateResult.Passed)
                    {
                        await EmitQualityGateRejected(entry, gateResult.Reason!, cancellationToken);
                        lastError = $"Quality gate rejected: {gateResult.Reason}";
                        await EmitFallbackTriggered(entry, lastError, orderedEntries, i, cancellationToken);
                        continue;
                    }
                }

                // Success — return with metadata about which provider served it
                return CloneResponseWithModel(
                    lastResponse,
                    $"{entry.Entry.Provider}/{entry.Entry.Model}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                await EmitFallbackTriggered(entry, lastError, orderedEntries, i, cancellationToken);
            }
        }

        // All providers exhausted
        await EmitFallbackExhausted(orderedEntries.Count, lastError, cancellationToken);

        return lastResponse ?? LlmResponse.Error(
            $"All {orderedEntries.Count} providers in fallback policy '{_policy.Name}' failed. Last error: {lastError ?? "unknown"}");
    }

    private static LlmRequest CloneRequestWithModel(LlmRequest request, string model)
    {
        return new LlmRequest
        {
            Model = model,
            Messages = request.Messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            StopSequence = request.StopSequence,
            OutputMode = request.OutputMode,
            JsonSchema = request.JsonSchema,
            SystemPrompt = request.SystemPrompt,
            Tools = request.Tools,
            SkipCache = request.SkipCache
        };
    }

    private static LlmResponse CloneResponseWithModel(LlmResponse response, string? model)
    {
        return new LlmResponse
        {
            Content = response.Content,
            Success = response.Success,
            ErrorMessage = response.ErrorMessage,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            Latency = response.Latency,
            Model = model,
            StopReason = response.StopReason,
            ToolCalls = response.ToolCalls
        };
    }

    private List<FallbackClientEntry> GetOrderedEntries()
    {
        return _policy.Strategy switch
        {
            FallbackStrategy.Failover => _entries.ToList(),
            FallbackStrategy.RoundRobin => GetRoundRobinOrder(),
            FallbackStrategy.Weighted => GetWeightedOrder(),
            FallbackStrategy.Split => GetSplitOrder(),
            _ => _entries.ToList()
        };
    }

    private List<FallbackClientEntry> GetRoundRobinOrder()
    {
        var index = (int)(Interlocked.Increment(ref _roundRobinCounter) % _entries.Count);
        var result = new List<FallbackClientEntry>(_entries.Count);

        // Start from the selected index, then wrap around for fallback
        for (var i = 0; i < _entries.Count; i++)
            result.Add(_entries[(index + i) % _entries.Count]);

        return result;
    }

    private List<FallbackClientEntry> GetWeightedOrder()
    {
        var totalWeight = _entries.Sum(e => e.Entry.Weight);
        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        var selectedIndex = 0;

        for (var i = 0; i < _entries.Count; i++)
        {
            cumulative += _entries[i].Entry.Weight;
            if (roll < cumulative)
            {
                selectedIndex = i;
                break;
            }
        }

        // Selected provider first, then remaining in original order for fallback
        var result = new List<FallbackClientEntry>(_entries.Count) { _entries[selectedIndex] };
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i != selectedIndex)
                result.Add(_entries[i]);
        }

        return result;
    }

    private List<FallbackClientEntry> GetSplitOrder()
    {
        var counter = Interlocked.Increment(ref _splitCounter);
        var totalWeight = _entries.Sum(e => e.Entry.Weight);
        var bucket = (int)(counter % totalWeight);
        var cumulative = 0;
        var selectedIndex = 0;

        for (var i = 0; i < _entries.Count; i++)
        {
            cumulative += _entries[i].Entry.Weight;
            if (bucket < cumulative)
            {
                selectedIndex = i;
                break;
            }
        }

        var result = new List<FallbackClientEntry>(_entries.Count) { _entries[selectedIndex] };
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i != selectedIndex)
                result.Add(_entries[i]);
        }

        return result;
    }

    private async Task EmitFallbackTriggered(
        FallbackClientEntry failedEntry,
        string? error,
        List<FallbackClientEntry> orderedEntries,
        int currentIndex,
        CancellationToken cancellationToken)
    {
        if (_eventSink is null || currentIndex + 1 >= orderedEntries.Count)
            return;

        var next = orderedEntries[currentIndex + 1];
        await _eventSink.PublishAsync(new FallbackTriggeredEvent
        {
            RunId = _runId,
            WorkflowId = _workflowId,
            NodeId = _nodeId,
            EventType = nameof(FallbackTriggeredEvent),
            FailedProvider = failedEntry.Entry.Provider,
            FailedModel = failedEntry.Entry.Model,
            ErrorMessage = error,
            NextProvider = next.Entry.Provider,
            NextModel = next.Entry.Model,
            AttemptIndex = currentIndex + 1,
            Strategy = _policy.Strategy.ToString(),
            PolicyName = _policy.Name
        }, cancellationToken);
    }

    private async Task EmitQualityGateRejected(
        FallbackClientEntry entry,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_eventSink is null)
            return;

        await _eventSink.PublishAsync(new QualityGateRejectedEvent
        {
            RunId = _runId,
            WorkflowId = _workflowId,
            NodeId = _nodeId,
            EventType = nameof(QualityGateRejectedEvent),
            Provider = entry.Entry.Provider,
            Model = entry.Entry.Model,
            Reason = reason,
            PolicyName = _policy.Name
        }, cancellationToken);
    }

    private async Task EmitFallbackExhausted(
        int totalAttempts,
        string? lastError,
        CancellationToken cancellationToken)
    {
        if (_eventSink is null)
            return;

        await _eventSink.PublishAsync(new FallbackExhaustedEvent
        {
            RunId = _runId,
            WorkflowId = _workflowId,
            NodeId = _nodeId,
            EventType = nameof(FallbackExhaustedEvent),
            TotalAttempts = totalAttempts,
            LastErrorMessage = lastError,
            PolicyName = _policy.Name,
            Strategy = _policy.Strategy.ToString()
        }, cancellationToken);
    }
}

/// <summary>
/// Pairs a resolved <see cref="ILlmClient"/> with its <see cref="FallbackProviderEntry"/> metadata.
/// </summary>
public sealed class FallbackClientEntry
{
    public required ILlmClient Client { get; init; }
    public required FallbackProviderEntry Entry { get; init; }
}