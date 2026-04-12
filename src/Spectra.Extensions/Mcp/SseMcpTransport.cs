using System.Threading.Channels;
using Spectra.Contracts.Mcp;
using Spectra.Extensions.Providers.Shared;

namespace Spectra.Extensions.Mcp;

/// <summary>
/// MCP transport using Server-Sent Events for server→client messages
/// and HTTP POST for client→server messages.
/// Reuses <see cref="SseReader"/> for SSE stream parsing.
/// </summary>
public sealed class SseMcpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
    private CancellationTokenSource? _sseLoopCts;
    private Task? _sseLoop;
    private string? _postEndpoint;
    private bool _disposed;

    public bool IsConnected => _sseLoopCts is not null && !_sseLoopCts.IsCancellationRequested;

    public SseMcpTransport(McpServerConfig config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        if (string.IsNullOrEmpty(config.Url))
            throw new ArgumentException("URL is required for SSE transport.", nameof(config));

        _httpClient = httpClient ?? new HttpClient();

        foreach (var (key, value) in config.Headers)
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
    }

    /// <summary>
    /// Connects to the SSE endpoint and starts reading server messages.
    /// The first SSE event with event type "endpoint" provides the POST URL.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _config.Url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        _sseLoopCts = new CancellationTokenSource();
        _sseLoop = Task.Run(async () =>
        {
            try
            {
                using var reader = new StreamReader(stream);
                string? currentEventType = null;

                while (!_sseLoopCts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_sseLoopCts.Token);
                    if (line is null) break;

                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        currentEventType = line["event: ".Length..];
                        continue;
                    }

                    if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        var data = line["data: ".Length..];

                        if (currentEventType == "endpoint")
                        {
                            // Resolve relative URL against the base SSE URL
                            var baseUri = new Uri(_config.Url!);
                            _postEndpoint = new Uri(baseUri, data).ToString();
                        }
                        else if (currentEventType == "message")
                        {
                            _inbound.Writer.TryWrite(data);
                        }

                        currentEventType = null;
                        continue;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _inbound.Writer.TryComplete();
            }
        }, _sseLoopCts.Token);

        // Wait briefly for the endpoint event
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_postEndpoint is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        if (_postEndpoint is null)
            throw new InvalidOperationException(
                $"MCP SSE server '{_config.Name}' did not send an endpoint event within 10 seconds.");
    }

    public async Task SendAsync(string jsonRpcMessage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_postEndpoint is null)
            throw new InvalidOperationException("SSE transport not connected — no POST endpoint received.");

        var content = new StringContent(jsonRpcMessage, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_postEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            return await _inbound.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _sseLoopCts?.Cancel();

        if (_sseLoop is not null)
        {
            try { await _sseLoop; }
            catch (OperationCanceledException) { }
        }

        _sseLoopCts?.Dispose();
        _httpClient.Dispose();
    }
}