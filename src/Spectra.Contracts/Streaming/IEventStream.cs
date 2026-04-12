using Spectra.Contracts.Events;

namespace Spectra.Contracts.Streaming;

public interface IEventStream
{
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(
        CancellationToken cancellationToken = default)
        where TEvent : WorkflowEvent;

    IAsyncEnumerable<WorkflowEvent> SubscribeAllAsync(
        StreamMode mode = StreamMode.Updates,
        CancellationToken cancellationToken = default);
}