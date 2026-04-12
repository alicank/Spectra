using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.Execution;

public interface IProviderRegistry
{
    void Register(ILlmProvider provider);
    ILlmClient? CreateClient(AgentDefinition agent);
    ILlmProvider? GetProvider(string name);
}