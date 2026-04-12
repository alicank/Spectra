using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.Execution;

public interface IAgentRegistry
{
    void Register(AgentDefinition agent);
    AgentDefinition? GetAgent(string agentId);
    IReadOnlyList<AgentDefinition> GetAll();
}