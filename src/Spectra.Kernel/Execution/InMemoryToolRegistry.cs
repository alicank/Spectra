using Spectra.Contracts.Events;
using Spectra.Contracts.Tools;
using Spectra.Kernel.Resilience;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Default in-memory implementation of <see cref="IToolRegistry"/>.
/// Thread-safe for concurrent reads; registration is expected at startup.
/// When a <see cref="DefaultToolResiliencePolicy"/> is set, newly registered tools
/// are automatically wrapped with <see cref="ResilientToolDecorator"/>.
/// </summary>
public class InMemoryToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private DefaultToolResiliencePolicy? _resiliencePolicy;
    private IEventSink? _resilienceEventSink;

    /// <summary>
    /// The active resilience policy, if tool circuit breaker is enabled.
    /// Exposed for DI resolution of <see cref="IToolResiliencePolicy"/>.
    /// </summary>
    internal DefaultToolResiliencePolicy? ResiliencePolicy => _resiliencePolicy;

    /// <summary>
    /// Configures automatic circuit breaker wrapping for all subsequently registered tools.
    /// Called during DI setup when <c>AddToolResilience</c> is used.
    /// </summary>
    internal void SetResiliencePolicy(DefaultToolResiliencePolicy policy, IEventSink? eventSink)
    {
        _resiliencePolicy = policy;
        _resilienceEventSink = eventSink;
    }

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        // If resilience is configured and the tool isn't already decorated, wrap it
        if (_resiliencePolicy is not null && tool is not ResilientToolDecorator)
        {
            tool = new ResilientToolDecorator(tool, _resiliencePolicy, this, _resilienceEventSink);
        }

        _tools[tool.Name] = tool;
    }

    public ITool? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IReadOnlyList<ITool> GetAll() => _tools.Values.ToList();

    public IReadOnlyList<ToolDefinition> GetDefinitions(IEnumerable<string>? filter = null)
    {
        if (filter is null)
            return _tools.Values.Select(t => t.Definition).ToList();

        var filterSet = new HashSet<string>(filter, StringComparer.OrdinalIgnoreCase);
        return _tools.Values
            .Where(t => filterSet.Contains(t.Name))
            .Select(t => t.Definition)
            .ToList();
    }
}