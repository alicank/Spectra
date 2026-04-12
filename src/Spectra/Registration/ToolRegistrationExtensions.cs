using Spectra.Contracts.Events;
using Spectra.Kernel.Mcp;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Execution;

namespace Spectra.Registration;

/// <summary>
/// Extension methods for populating an <see cref="IToolRegistry"/> from a <see cref="SpectraBuilder"/>.
/// </summary>
public static class ToolRegistrationExtensions
{
    /// <summary>
    /// Builds an <see cref="IToolRegistry"/> from all tools and assemblies registered in the builder.
    /// Explicitly registered tools take precedence over auto-discovered ones.
    /// </summary>
    public static IToolRegistry BuildToolRegistry(this SpectraBuilder builder)
    {
        var registry = new InMemoryToolRegistry();

        // 1. Auto-discover from registered assemblies
        foreach (var assembly in builder.ToolAssemblies)
        {
            var discovered = ToolDiscovery.DiscoverTools(assembly);
            foreach (var tool in discovered)
                registry.Register(tool);
        }

        // 2. Explicit registrations override auto-discovered ones
        foreach (var tool in builder.Tools)
            registry.Register(tool);

        return registry;
    }

    /// <summary>
    /// Initialises all registered MCP servers, discovers their tools,
    /// and registers the adapted tools into the given registry.
    /// Returns the providers so the caller can manage their lifecycle.
    /// </summary>
    public static async Task<IReadOnlyList<McpToolProvider>> RegisterMcpToolsAsync(
        this SpectraBuilder builder,
        IToolRegistry registry,
        IEventSink? eventSink = null,
        CancellationToken cancellationToken = default)
    {
        var providers = new List<McpToolProvider>();

        foreach (var config in builder.McpServers)
        {
            var provider = new McpToolProvider(config, eventSink);
            await provider.InitializeAsync(cancellationToken);
            provider.RegisterTools(registry);
            providers.Add(provider);
        }

        return providers;
    }
}