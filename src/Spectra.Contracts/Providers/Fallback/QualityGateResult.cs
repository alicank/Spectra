namespace Spectra.Contracts.Providers.Fallback;

/// <summary>
/// Result of a quality gate evaluation on a fallback response.
/// </summary>
public record QualityGateResult
{
    public bool Passed { get; init; }
    public string? Reason { get; init; }

    public static QualityGateResult Pass() => new() { Passed = true };
    public static QualityGateResult Fail(string reason) => new() { Passed = false, Reason = reason };
}