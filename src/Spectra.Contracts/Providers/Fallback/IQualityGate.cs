namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Validates that a fallback response meets minimum quality criteria
/// before the system accepts it. Prevents silently serving garbage
/// from a weaker model.
/// </summary>
public interface IQualityGate
{
    /// <summary>
    /// Evaluates whether the response is acceptable.
    /// </summary>
    QualityGateResult Evaluate(LlmResponse response);
}