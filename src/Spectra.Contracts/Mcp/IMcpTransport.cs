namespace Spectra.Contracts.Mcp;

/// <summary>
/// Abstraction over the MCP transport layer (stdio, SSE, streamable-HTTP).
/// Implementations handle the physical I/O; <see cref="IMcpClient"/> handles JSON-RPC framing.
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>
    /// Sends a raw JSON-RPC message to the MCP server.
    /// </summary>
    Task SendAsync(string jsonRpcMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives the next raw JSON-RPC message from the MCP server.
    /// Returns null when the transport is closed or the server disconnects.
    /// </summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the transport connection is still alive.
    /// </summary>
    bool IsConnected { get; }
}