using Spectra.Contracts.Execution;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Default in-memory implementation of <see cref="IAgentRegistry"/>.
/// Thread-safe for concurrent reads; registration is expected at startup.
/// </summary>
public class InMemoryAgentRegistry : IAgentRegistry
{
    private readonly Dictionary<string, AgentDefinition> _agents = new(StringComparer.OrdinalIgnoreCase);

    public void Register(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents[agent.Id] = agent;
    }

    public AgentDefinition? GetAgent(string agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return _agents.TryGetValue(agentId, out var agent) ? agent : null;
    }

    public IReadOnlyList<AgentDefinition> GetAll() => _agents.Values.ToList();
}