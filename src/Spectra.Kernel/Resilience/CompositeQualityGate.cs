using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Chains multiple quality gates. All gates must pass for the response to be accepted.
/// </summary>
public sealed class CompositeQualityGate : IQualityGate
{
    private readonly IReadOnlyList<IQualityGate> _gates;

    public CompositeQualityGate(IEnumerable<IQualityGate> gates)
    {
        _gates = gates.ToList();
    }

    public CompositeQualityGate(params IQualityGate[] gates)
    {
        _gates = gates;
    }

    public QualityGateResult Evaluate(LlmResponse response)
    {
        foreach (var gate in _gates)
        {
            var result = gate.Evaluate(response);
            if (!result.Passed)
                return result;
        }

        return QualityGateResult.Pass();
    }
}