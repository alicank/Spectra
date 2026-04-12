using System.Threading.Channels;

namespace Spectra.Contracts.Events;

/// <summary>
/// An <see cref="IEventSink"/> that writes workflow events into a
/// <see cref="Channel{T}"/> for consumption as <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public sealed class StreamingEventSink : IEventSink
{
    private readonly ChannelWriter<WorkflowEvent> _writer;

    public StreamingEventSink(ChannelWriter<WorkflowEvent> writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
    {
        await _writer.WriteAsync(evt, cancellationToken);
    }

    /// <summary>
    /// Signals that no more events will be written.
    /// </summary>
    public void Complete(Exception? error = null)
    {
        _writer.TryComplete(error);
    }
}