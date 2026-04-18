using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Spectra.Contracts.Mcp;

namespace Spectra.Extensions.Mcp;

/// <summary>
/// MCP transport implementing the Streamable HTTP protocol (2025-03-26 spec).
/// Uses HTTP POST for all client→server messages and handles both
/// JSON and SSE response modes. Optionally opens a GET-based SSE stream
/// for server-initiated messages.
///
/// Drop-in replacement for <see cref="SseMcpTransport"/> — implements the
/// same <see cref="IMcpTransport"/> interface.
/// </summary>
public sealed class StreamableHttpMcpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
    private readonly string _endpoint;

    private string? _sessionId;
    private CancellationTokenSource? _getSseLoopCts;
    private Task? _getSseLoop;
    private bool _connected;
    private bool _disposed;

    public bool IsConnected => _connected && !_disposed;

    public StreamableHttpMcpTransport(McpServerConfig config, HttpClient? httpClient = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        if (string.IsNullOrEmpty(config.Url))
            throw new ArgumentException("URL is required for Streamable HTTP transport.", nameof(config));

        _endpoint = config.Url!.TrimEnd('/');

        _httpClient = httpClient ?? new HttpClient();

        foreach (var (key, value) in config.Headers)
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
    }

    /// <summary>
    /// Marks the transport as connected. The actual MCP handshake (initialize)
    /// is performed by the MCP client layer via SendAsync/ReceiveAsync — this
    /// transport just needs to be ready to POST.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a JSON-RPC message via HTTP POST to the MCP endpoint.
    /// Handles both application/json and text/event-stream responses.
    /// Captures Mcp-Session-Id from the server on the first response.
    /// </summary>
    public async Task SendAsync(string jsonRpcMessage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connected)
            throw new InvalidOperationException("Transport not connected.");

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(jsonRpcMessage, Encoding.UTF8, "application/json")
        };

        // Required Accept header per spec: client must accept both
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Include session ID on all requests after the server provides one
        if (_sessionId is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

        var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Capture session ID from any response that includes it
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            _sessionId = sessionValues.FirstOrDefault() ?? _sessionId;
        }

        // 202 Accepted = notification/response acknowledged, no body
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            return;

        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // SSE streaming response — parse events and enqueue JSON-RPC messages
            await ReadSseResponseAsync(response, cancellationToken);
        }
        else
        {
            // Plain JSON response — enqueue the entire body as one message
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
                _inbound.Writer.TryWrite(body);
        }

        // After first response with a session ID, optionally open the GET SSE stream
        // for server-initiated messages (notifications, sampling requests, etc.)
        if (_getSseLoop is null && _sessionId is not null)
        {
            TryOpenGetSseStream();
        }
    }

    /// <summary>
    /// Reads the next inbound JSON-RPC message (from either POST responses or the GET SSE stream).
    /// </summary>
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

    // ─── SSE response parsing ────────────────────────────────────────

    /// <summary>
    /// Reads an SSE response stream from a POST response body.
    /// Each "data:" line containing a JSON-RPC message is enqueued to _inbound.
    /// Per the spec, the SSE event type defaults to "message" when not specified.
    /// </summary>
    private async Task ReadSseResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var dataBuffer = new StringBuilder();
        string? currentEventType = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break; // stream closed

            // Empty line = end of SSE event
            if (line.Length == 0)
            {
                if (dataBuffer.Length > 0)
                {
                    var data = dataBuffer.ToString();
                    dataBuffer.Clear();

                    // "message" or no event type → enqueue as JSON-RPC message
                    if (currentEventType is null or "message")
                    {
                        _inbound.Writer.TryWrite(data);
                    }
                }
                currentEventType = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEventType = line["event:".Length..].TrimStart();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuffer.Length > 0)
                    dataBuffer.Append('\n');
                dataBuffer.Append(line["data:".Length..].TrimStart());
            }
            // "id:" and "retry:" lines are ignored for now
        }

        // Flush any trailing event that wasn't terminated by a blank line
        if (dataBuffer.Length > 0 && currentEventType is null or "message")
        {
            _inbound.Writer.TryWrite(dataBuffer.ToString());
        }
    }

    // ─── GET-based SSE stream for server-initiated messages ──────────

    /// <summary>
    /// Opens an optional long-lived GET request to receive server-initiated
    /// messages (notifications, sampling requests, etc.).
    /// If the server returns 405, it simply doesn't support this — that's fine.
    /// </summary>
    private void TryOpenGetSseStream()
    {
        _getSseLoopCts = new CancellationTokenSource();
        var ct = _getSseLoopCts.Token;

        _getSseLoop = Task.Run(async () =>
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("text/event-stream"));

                if (_sessionId is not null)
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

                var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);

                // 405 = server doesn't offer a GET SSE stream — perfectly valid
                if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                    return;

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var dataBuffer = new StringBuilder();
                string? currentEventType = null;

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    if (line.Length == 0)
                    {
                        if (dataBuffer.Length > 0)
                        {
                            var data = dataBuffer.ToString();
                            dataBuffer.Clear();

                            if (currentEventType is null or "message")
                            {
                                _inbound.Writer.TryWrite(data);
                            }
                        }
                        currentEventType = null;
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.Ordinal))
                        currentEventType = line["event:".Length..].TrimStart();
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                        dataBuffer.Append(line["data:".Length..].TrimStart());
                    }
                }
            }
            catch (OperationCanceledException) { /* expected on shutdown */ }
            catch (HttpRequestException) { /* server may not support GET stream — fine */ }
        }, ct);
    }

    // ─── Cleanup ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;

        _getSseLoopCts?.Cancel();

        if (_getSseLoop is not null)
        {
            try { await _getSseLoop; }
            catch (OperationCanceledException) { }
        }

        _getSseLoopCts?.Dispose();
        _inbound.Writer.TryComplete();
        _httpClient.Dispose();
    }
}