using Spectra.Contracts.Events;

namespace Spectra.Registration;

/// <summary>
/// No-op event sink used when no sinks are configured.
/// Avoids null checks throughout the pipeline.
/// </summary>
internal sealed class NullEventSink : IEventSink
{
    public static readonly NullEventSink Instance = new();

    private NullEventSink() { }

    public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}