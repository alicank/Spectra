using Spectra.Contracts.Workflow;

namespace Spectra.Contracts.Providers;

public interface ILlmProvider
{
    string Name { get; }
    ILlmClient CreateClient(AgentDefinition agent);
    bool SupportsModel(string modelId);
}