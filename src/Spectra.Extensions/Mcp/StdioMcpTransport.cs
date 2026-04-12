using System.Diagnostics;
using Spectra.Contracts.Mcp;

namespace Spectra.Extensions.Mcp;

/// <summary>
/// MCP transport that spawns a child process and communicates over stdin/stdout.
/// Each line on stdout is treated as a complete JSON-RPC message.
/// </summary>
public sealed class StdioMcpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _disposed;

    public bool IsConnected => _process is not null && !_process.HasExited;

    public StdioMcpTransport(McpServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        if (string.IsNullOrEmpty(config.Command))
            throw new ArgumentException("Command is required for stdio transport.", nameof(config));
    }

    /// <summary>
    /// Starts the child process. Must be called before Send/Receive.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _config.Command!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _config.WorkingDirectory ?? Environment.CurrentDirectory
        };

        foreach (var arg in _config.Arguments)
            psi.ArgumentList.Add(arg);

        if (!_config.InheritEnvironment)
            psi.Environment.Clear();

        foreach (var (key, value) in _config.EnvironmentVariables)
            psi.Environment[key] = value;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start MCP server process: {_config.Command}");

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        // Ensure encoding is UTF-8 without BOM
        _stdin.AutoFlush = true;

        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonRpcMessage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stdin is null)
            throw new InvalidOperationException("Transport not started.");

        await _stdin.WriteLineAsync(jsonRpcMessage.AsMemory(), cancellationToken);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stdout is null)
            throw new InvalidOperationException("Transport not started.");

        try
        {
            var line = await _stdout.ReadLineAsync(cancellationToken);
            return line;
        }
        catch (IOException) when (_process?.HasExited == true)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_stdin is not null)
        {
            try
            {
                await _stdin.DisposeAsync();
            }
            catch { /* best effort */ }
        }

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { /* best effort */ }
        }

        _process?.Dispose();
    }
}