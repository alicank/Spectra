namespace Spectra.Contracts.Steps;

public enum StepStatus
{
    Succeeded,
    Failed,
    Skipped,
    NeedsContinuation,
    Interrupted,
    Handoff,
    AwaitingInput
}
