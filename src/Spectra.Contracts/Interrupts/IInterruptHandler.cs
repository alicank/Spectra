namespace Spectra.Contracts.Interrupts;

public interface IInterruptHandler
{
    Task<InterruptResponse> HandleAsync(
        InterruptRequest request,
        CancellationToken cancellationToken = default);
}