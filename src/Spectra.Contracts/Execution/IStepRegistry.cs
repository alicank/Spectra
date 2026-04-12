using Spectra.Contracts.Steps;

namespace Spectra.Contracts.Execution;

public interface IStepRegistry
{
    IStep? GetStep(string stepType);
    void Register(IStep step);
}