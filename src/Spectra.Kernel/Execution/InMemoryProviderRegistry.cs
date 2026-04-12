using Spectra.Contracts.Execution;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Default in-memory implementation of <see cref="IProviderRegistry"/>.
/// Thread-safe for concurrent reads; registration is expected at startup.
/// </summary>
internal class InMemoryProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.Name] = provider;
    }

    public ILlmClient? CreateClient(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (_providers.TryGetValue(agent.Provider, out var provider))
            return provider.CreateClient(agent);

        // Fallback: find any provider that supports the requested model
        var match = _providers.Values.FirstOrDefault(p => p.SupportsModel(agent.Model));
        return match?.CreateClient(agent);
    }

    public ILlmProvider? GetProvider(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _providers.TryGetValue(name, out var provider) ? provider : null;
    }
}