using Spectra.Contracts.Execution;
using Spectra.Contracts.Steps;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Default in-memory implementation of <see cref="IStepRegistry"/>.
/// Thread-safe for concurrent reads; registration is expected at startup.
/// </summary>
internal class InMemoryStepRegistry : IStepRegistry
{
    private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps[step.StepType] = step;
    }

    public IStep? GetStep(string stepType)
    {
        ArgumentNullException.ThrowIfNull(stepType);
        return _steps.TryGetValue(stepType, out var step) ? step : null;
    }
}