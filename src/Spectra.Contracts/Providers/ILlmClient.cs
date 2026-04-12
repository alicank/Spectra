namespace Spectra.Contracts.Providers;

public interface ILlmClient
{
    string ProviderName { get; }
    string ModelId { get; }
    ModelCapabilities Capabilities { get; }

    Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}