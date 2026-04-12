namespace Spectra.Contracts.Events;

public sealed class CompositeEventSink : IEventSink
{
    private readonly IReadOnlyList<IEventSink> _sinks;

    public CompositeEventSink(IEnumerable<IEventSink> sinks)
    {
        _sinks = sinks?.ToList() ?? throw new ArgumentNullException(nameof(sinks));
    }

    public async Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
    {
        var tasks = _sinks.Select(sink => sink.PublishAsync(evt, cancellationToken));
        await Task.WhenAll(tasks);
    }
}
