namespace Spectra.Contracts.Steps;

public interface IStep
{
    string StepType { get; }
    Task<StepResult> ExecuteAsync(StepContext context);
}
