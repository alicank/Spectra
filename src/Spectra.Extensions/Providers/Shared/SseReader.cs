using System.Runtime.CompilerServices;

namespace Spectra.Extensions.Providers.Shared;

/// <summary>
/// Reads a server-sent-events stream and yields the value of each "data:" line.
/// Stops when it encounters the sentinel "data: [DONE]" or the stream ends.
/// </summary>
internal static class SseReader
{
    internal static async IAsyncEnumerable<string> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                yield break;

            if (line.Length == 0 || line.StartsWith(':'))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var payload = line["data: ".Length..];

            if (payload is "[DONE]")
                yield break;

            yield return payload;
        }
    }
}