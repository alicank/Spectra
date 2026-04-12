namespace Spectra.Contracts.Events;

public interface IEventSink
{
    Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default);
}