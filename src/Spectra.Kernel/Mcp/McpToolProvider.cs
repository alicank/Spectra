using Spectra.Contracts.Events;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.Tools;

namespace Spectra.Kernel.Mcp;

/// <summary>
/// Manages the full lifecycle of an MCP server: transport creation,
/// client initialization, tool discovery, filtering, and adapter creation.
/// Implements <see cref="IAsyncDisposable"/> for clean shutdown.
/// </summary>
public sealed class McpToolProvider : IAsyncDisposable
{
    private readonly McpServerConfig _config;
    private readonly IEventSink? _eventSink;
    private McpClient? _client;
    private readonly McpCallTracker _callTracker = new();
    private List<McpToolAdapter> _adapters = [];
    private bool _disposed;

    public string ServerName => _config.Name;

    /// <summary>
    /// The adapted tools available after <see cref="InitializeAsync"/>.
    /// </summary>
    public IReadOnlyList<ITool> Tools => _adapters;

    public McpToolProvider(McpServerConfig config, IEventSink? eventSink = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _eventSink = eventSink;
    }

    /// <summary>
    /// Creates the transport, initialises the MCP client, discovers tools,
    /// applies filtering (allowed/denied/read-only), and creates adapters.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var transport = await CreateTransportAsync(cancellationToken);
        _client = new McpClient(transport, _config);
        await _client.InitializeAsync(cancellationToken);

        // Filter and adapt discovered tools
        _adapters = _client.Tools
            .Where(IsToolAllowed)
            .Select(t => new McpToolAdapter(_client, t, _config, _callTracker, _eventSink))
            .ToList();

        // Emit connected event
        if (_eventSink is not null)
        {
            await _eventSink.PublishAsync(new McpServerConnectedEvent
            {
                RunId = string.Empty,
                WorkflowId = string.Empty,
                EventType = "mcp.server_connected",
                ServerName = _config.Name,
                Transport = _config.Transport.ToString().ToLowerInvariant(),
                ToolCount = _adapters.Count,
                ToolNames = _adapters.Select(a => a.McpToolName).ToList()
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Registers all discovered (and allowed) tools into the given registry.
    /// </summary>
    public void RegisterTools(IToolRegistry registry)
    {
        foreach (var adapter in _adapters)
            registry.Register(adapter);
    }

    private bool IsToolAllowed(McpToolInfo tool)
    {
        // Allowed list (if specified, only these tools pass)
        if (_config.AllowedTools is { Count: > 0 })
        {
            if (!_config.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // Denied list
        if (_config.DeniedTools is { Count: > 0 })
        {
            if (_config.DeniedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // Read-only filter
        if (_config.ReadOnly && tool.Annotations is { ReadOnlyHint: false })
            return false;

        return true;
    }

    private async Task<IMcpTransport> CreateTransportAsync(CancellationToken cancellationToken)
    {
        switch (_config.Transport)
        {
            case McpTransportType.Stdio:
                {
                    var transport = new Extensions.Mcp.StdioMcpTransport(_config);
                    await transport.StartAsync(cancellationToken);
                    return transport;
                }

            case McpTransportType.Sse:
                {
                    var transport = new Extensions.Mcp.SseMcpTransport(_config);
                    await transport.ConnectAsync(cancellationToken);
                    return transport;
                }

            case McpTransportType.Http:
                throw new NotSupportedException(
                    "Streamable-HTTP transport is not yet implemented. Use SSE or Stdio.");

            default:
                throw new ArgumentOutOfRangeException(nameof(_config.Transport));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client is not null)
        {
            if (_eventSink is not null)
            {
                try
                {
                    await _eventSink.PublishAsync(new McpServerDisconnectedEvent
                    {
                        RunId = string.Empty,
                        WorkflowId = string.Empty,
                        EventType = "mcp.server_disconnected",
                        ServerName = _config.Name,
                        Reason = "normal"
                    });
                }
                catch { /* best effort */ }
            }

            await _client.DisposeAsync();
        }
    }
}