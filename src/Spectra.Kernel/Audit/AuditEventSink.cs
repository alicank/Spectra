using System.Diagnostics;
using System.Text.Json;
using Spectra.Contracts.Audit;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;

namespace Spectra.Kernel.Audit;

/// <summary>
/// An <see cref="IEventSink"/> decorator that converts workflow events into
/// immutable <see cref="AuditEntry"/> records and writes them to an <see cref="IAuditStore"/>.
/// Captures OpenTelemetry trace/span IDs from <see cref="Activity.Current"/> for correlation.
/// </summary>
public sealed class AuditEventSink : IEventSink
{
    private readonly IAuditStore _auditStore;
    private readonly Func<RunContext?> _runContextAccessor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <param name="auditStore">The backing audit store to write entries to.</param>
    /// <param name="runContextAccessor">
    /// Callback that returns the current <see cref="RunContext"/>.
    /// Captures identity at event-write time, not at sink construction time.
    /// </param>
    public AuditEventSink(IAuditStore auditStore, Func<RunContext?> runContextAccessor)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _runContextAccessor = runContextAccessor ?? throw new ArgumentNullException(nameof(runContextAccessor));
    }

    public async Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
    {
        var runContext = _runContextAccessor();
        var activity = Activity.Current;

        var entry = new AuditEntry
        {
            Id = evt.EventId.ToString(),
            Timestamp = evt.Timestamp,
            TenantId = evt.TenantId ?? runContext?.TenantId,
            UserId = evt.UserId ?? runContext?.UserId,
            RunId = evt.RunId,
            WorkflowId = evt.WorkflowId,
            NodeId = evt.NodeId,
            EventType = evt.EventType,
            EventData = SerializeEvent(evt),
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString()
        };

        await _auditStore.WriteAsync(entry, cancellationToken);
    }

    private static string SerializeEvent(WorkflowEvent evt)
    {
        try
        {
            return JsonSerializer.Serialize<object>(evt, JsonOptions);
        }
        catch
        {
            return $"{{\"eventType\":\"{evt.EventType}\",\"error\":\"serialization_failed\"}}";
        }
    }
}