using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Contracts.Events;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Spectra.Kernel.Mcp;
using System.Reflection;

namespace Spectra.Registration;

/// <summary>
/// Hosted service that initialises MCP server connections at application startup
/// and registers discovered tools into the <see cref="IToolRegistry"/>.
/// Follows the standard .NET <see cref="IHostedService"/> pattern for async bootstrap.
/// </summary>
internal sealed class SpectraHostedService : IHostedService, IAsyncDisposable
{
    private readonly IReadOnlyList<McpServerConfig> _mcpConfigs;
    private readonly IReadOnlyList<Assembly> _toolAssemblies;
    private readonly IToolRegistry _toolRegistry;
    private readonly IEventSink _eventSink;
    private readonly ILogger<SpectraHostedService> _logger;
    private readonly IAgentRegistry? _agentRegistry;
    private readonly List<McpToolProvider> _providers = [];
    private bool _disposed;

    public SpectraHostedService(
        IReadOnlyList<McpServerConfig> mcpConfigs,
        IReadOnlyList<Assembly> toolAssemblies,
        IToolRegistry toolRegistry,
        IEventSink eventSink,
        ILogger<SpectraHostedService> logger,
        IAgentRegistry? agentRegistry = null)
    {
        _mcpConfigs = mcpConfigs ?? throw new ArgumentNullException(nameof(mcpConfigs));
        _toolAssemblies = toolAssemblies ?? throw new ArgumentNullException(nameof(toolAssemblies));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _eventSink = eventSink;
        _logger = logger ?? NullLogger<SpectraHostedService>.Instance;
        _agentRegistry = agentRegistry;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // --- Assembly-based tool auto-discovery ---
        foreach (var assembly in _toolAssemblies)
        {
            try
            {
                var discovered = ToolDiscovery.DiscoverTools(assembly);
                foreach (var tool in discovered)
                    _toolRegistry.Register(tool);

                _logger.LogInformation(
                    "Discovered {ToolCount} tool(s) from assembly {Assembly}",
                    discovered.Count,
                    assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to discover tools from assembly {Assembly}",
                    assembly.GetName().Name);
                throw;
            }
        }

        // --- MCP server connections ---
        foreach (var config in _mcpConfigs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation(
                    "Connecting to MCP server '{ServerName}' via {Transport}...",
                    config.Name,
                    config.Transport);

                var provider = new McpToolProvider(config, _eventSink);
                await provider.InitializeAsync(cancellationToken);
                provider.RegisterTools(_toolRegistry);
                _providers.Add(provider);

                _logger.LogInformation(
                    "MCP server '{ServerName}' connected — {ToolCount} tool(s) registered",
                    config.Name,
                    provider.Tools.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to connect to MCP server '{ServerName}'. " +
                    "The server will be unavailable for this application session",
                    config.Name);
                throw;
            }
        }

        // --- Agent tool declaration validation (SP-65) ---
        if (_agentRegistry is not null)
        {
            var allAgents = _agentRegistry.GetAll();
            foreach (var agent in allAgents)
            {
                if (agent.Tools.Count == 0)
                    continue;

                foreach (var toolName in agent.Tools)
                {
                    var tool = _toolRegistry.Get(toolName);
                    if (tool is null)
                    {
                        _logger.LogWarning(
                            "Agent '{AgentId}' declares tool '{ToolName}' which is not registered in the tool registry. " +
                            "This will cause a runtime failure when the agent tries to use this tool",
                            agent.Id, toolName);
                    }
                }

                _logger.LogInformation(
                    "Agent '{AgentId}' tool validation complete — {ValidCount}/{TotalCount} tools resolved",
                    agent.Id,
                    agent.Tools.Count(t => _toolRegistry.Get(t) is not null),
                    agent.Tools.Count);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down Spectra hosted service...");
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var provider in _providers)
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error disposing MCP server '{ServerName}'",
                    provider.ServerName);
            }
        }

        _providers.Clear();
    }
}