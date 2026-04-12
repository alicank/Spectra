using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Quality gate that rejects responses shorter than a minimum character length.
/// Useful for catching empty or truncated responses from weaker models.
/// </summary>
public sealed class MinLengthQualityGate : IQualityGate
{
    private readonly int _minimumLength;

    public MinLengthQualityGate(int minimumLength = 10)
    {
        _minimumLength = minimumLength;
    }

    public QualityGateResult Evaluate(LlmResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Content))
            return QualityGateResult.Fail("Response content is empty or whitespace.");

        if (response.Content.Length < _minimumLength)
            return QualityGateResult.Fail(
                $"Response length {response.Content.Length} is below minimum {_minimumLength}.");

        return QualityGateResult.Pass();
    }
}