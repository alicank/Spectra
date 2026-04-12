namespace Spectra.Contracts.Evaluation;

public class ConditionResult
{
    public bool Satisfied { get; init; }
    public string? Reason { get; init; }

    public static ConditionResult True(string? reason = null) =>
        new() { Satisfied = true, Reason = reason };

    public static ConditionResult False(string? reason = null) =>
        new() { Satisfied = false, Reason = reason };
}