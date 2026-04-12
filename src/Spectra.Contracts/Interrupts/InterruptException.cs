namespace Spectra.Contracts.Interrupts;

/// <summary>
/// Thrown by <see cref="Steps.StepContext.InterruptAsync"/> when no interrupt handler
/// is configured or the handler cannot resolve the interrupt synchronously.
/// The workflow runner catches this to checkpoint and suspend execution.
/// </summary>
public sealed class InterruptException : Exception
{
    public InterruptRequest Request { get; }

    public InterruptException(InterruptRequest request)
        : base($"Interrupt requested at node '{request.NodeId}': {request.Reason ?? "(no reason)"}")
    {
        Request = request;
    }
}