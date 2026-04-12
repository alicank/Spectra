using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Spectra.Contracts.Diagnostics;
using Spectra.Contracts.Mcp;
using Spectra.Kernel.Diagnostics;

namespace Spectra.Kernel.Mcp;

/// <summary>
/// MCP protocol client. Speaks JSON-RPC 2.0 over an <see cref="IMcpTransport"/>.
/// Handles lifecycle (initialize → tools/list → tools/call) and request/response correlation.
/// Thread-safe: concurrent <see cref="CallToolAsync"/> calls are serialised for stdio
/// or run in parallel for HTTP-based transports.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    private readonly IMcpTransport _transport;
    private readonly McpServerConfig _config;
    private readonly SemaphoreSlim _sendLock;
    private readonly Dictionary<int, TaskCompletionSource<JsonRpcResponse>> _pending = new();
    private readonly CancellationTokenSource _readLoopCts = new();
    private readonly object _pendingLock = new();
    private int _nextId = 1;
    private Task? _readLoop;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Server name from configuration.
    /// </summary>
    public string ServerName => _config.Name;

    /// <summary>
    /// Tools discovered after initialization.
    /// </summary>
    public IReadOnlyList<McpToolInfo> Tools { get; private set; } = [];

    public McpClient(IMcpTransport transport, McpServerConfig config)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Stdio needs serialised sends; HTTP-based transports can be concurrent
        _sendLock = config.Transport == McpTransportType.Stdio
            ? new SemaphoreSlim(1, 1)
            : new SemaphoreSlim(int.MaxValue, int.MaxValue);
    }

    /// <summary>
    /// Performs the MCP initialize handshake and discovers available tools.
    /// Must be called once before any <see cref="CallToolAsync"/> invocation.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            throw new InvalidOperationException("MCP client is already initialized.");

        // Start background read loop
        _readLoop = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), cancellationToken);

        // 1. Send initialize
        var initResult = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new
            {
                name = "Spectra",
                version = "1.0.0"
            }
        }, cancellationToken);

        if (!initResult.IsSuccess)
            throw new InvalidOperationException(
                $"MCP initialize failed for '{_config.Name}': {initResult.Error?.Message ?? "unknown error"}");

        // 2. Send initialized notification (no id = notification)
        await SendNotificationAsync("notifications/initialized", null, cancellationToken);

        // 3. Discover tools
        var toolsResult = await SendRequestAsync("tools/list", new { }, cancellationToken);
        if (toolsResult.IsSuccess && toolsResult.Result is not null)
        {
            Tools = ParseToolList(toolsResult.Result.Value);
        }

        _initialized = true;
    }

    /// <summary>
    /// Invokes a tool on the MCP server and returns the raw JSON-RPC result.
    /// </summary>
    public async Task<JsonRpcResponse> CallToolAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var activity = SpectraActivitySource.Source.StartActivity("mcp.tool_call", ActivityKind.Client);
        activity?.SetTag(McpTags.ServerName, _config.Name);
        activity?.SetTag(McpTags.ToolName, toolName);
        activity?.SetTag(McpTags.Transport, _config.Transport.ToString().ToLowerInvariant());

        try
        {
            var result = await SendRequestAsync("tools/call", new
            {
                name = toolName,
                arguments
            }, cancellationToken);

            activity?.SetTag(McpTags.CallSuccess, result.IsSuccess);
            if (!result.IsSuccess)
            {
                activity?.SetTag(McpTags.ErrorCode, result.Error?.Code);
                SpectraActivitySource.RecordError(activity, result.Error?.Message ?? "MCP tool call failed");
            }

            return result;
        }
        catch (Exception ex)
        {
            SpectraActivitySource.RecordError(activity, ex);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private async Task<JsonRpcResponse> SendRequestAsync(
        string method, object? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params
        };

        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pending[id] = tcs;
        }

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _transport.SendAsync(json, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }

        return await tcs.Task;
    }

    private async Task SendNotificationAsync(
        string method, object? @params, CancellationToken cancellationToken)
    {
        var notification = new JsonRpcRequest
        {
            Id = null, // Notifications have no id
            Method = method,
            Params = @params
        };

        var json = JsonSerializer.Serialize(notification, SerializerOptions);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _transport.SendAsync(json, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
            {
                var raw = await _transport.ReceiveAsync(cancellationToken);
                if (raw is null)
                    break;

                try
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(raw, SerializerOptions);
                    if (response?.Id is { } id)
                    {
                        TaskCompletionSource<JsonRpcResponse>? tcs;
                        lock (_pendingLock)
                        {
                            _pending.Remove(id, out tcs);
                        }

                        tcs?.TrySetResult(response);
                    }
                    // Notifications (no id) are currently ignored — future: tools/list_changed
                }
                catch (JsonException)
                {
                    // Malformed response — skip
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            // Cancel all pending requests
            lock (_pendingLock)
            {
                foreach (var tcs in _pending.Values)
                    tcs.TrySetCanceled();
                _pending.Clear();
            }
        }
    }

    private static IReadOnlyList<McpToolInfo> ParseToolList(JsonElement result)
    {
        if (result.TryGetProperty("tools", out var toolsArray)
            && toolsArray.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<McpToolInfo>>(toolsArray.GetRawText(), SerializerOptions)
                   ?? [];
        }

        return [];
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                $"MCP client '{_config.Name}' has not been initialized. Call InitializeAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _readLoopCts.Cancel();

        if (_readLoop is not null)
        {
            try { await _readLoop; }
            catch (OperationCanceledException) { }
        }

        await _transport.DisposeAsync();
        _readLoopCts.Dispose();
        _sendLock.Dispose();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}