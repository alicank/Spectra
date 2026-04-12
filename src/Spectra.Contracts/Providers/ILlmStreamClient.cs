namespace Spectra.Contracts.Providers;

/// <summary>
/// Extends the LLM client contract with server-sent-event style streaming.
/// Implementations yield text chunks as they arrive from the provider.
/// </summary>
public interface ILlmStreamClient : ILlmClient
{
    /// <summary>
    /// Streams completion tokens as they are generated.
    /// Each yielded string is a text delta (partial token / word).
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}